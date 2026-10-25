#!/usr/bin/env ruby
# Adds nCTRL's widget and its "Send to car" Share extension to the Xcode project Godot exports, with an App Group
# shared by the app and both extensions ("group.<app bundle ID>"). The Share extension sends to the car with nCTRL's C#
# Core, built here with NativeAOT (Native/iOS/CoreLib) into NctrlCore.xcframework (with its debug symbols), embedded
# in the app. Godot regenerates
# the project on every export, so run this after each one:
#
#   ruby Native/iOS/add-extensions.rb <folder that holds nCTRL.xcodeproj>
#
# Needs the xcodeproj gem and the .NET SDK. Team, bundle ID and version come from the exported app target; nothing personal is
# kept in this repository.
require 'xcodeproj'
require 'fileutils'

export = File.expand_path(ARGV[0] || abort('Usage: add-extensions.rb <folder that holds nCTRL.xcodeproj>'))
project = Xcodeproj::Project.open(File.join(export, 'nCTRL.xcodeproj'))
app = project.targets.find { |t| t.name == 'nCTRL' } or abort('No nCTRL app target in this project.')
source = File.join(__dir__, 'Extensions')
EXTENSIONS = [
  { name: 'nCTRLWidget', suffix: 'widget', folder: 'Widget' },
  { name: 'nCTRLShare', suffix: 'share', folder: 'Share' }
].freeze
if project.targets.any? { |t| EXTENSIONS.any? { |x| x[:name] == t.name } }
  puts 'The extensions are already in this project.'
  exit 0
end

release = app.build_configurations.find { |c| c.name == 'Release' } || app.build_configurations.first
bundle = release.build_settings['PRODUCT_BUNDLE_IDENTIFIER'] or abort('The app target has no bundle ID.')
app_group = "group.#{bundle}"

# nCTRL's Core for the Share extension, as a framework next to the project.
build = File.join(__dir__, 'build')
FileUtils.mkdir_p(build)
FileUtils.touch(File.join(build, '.gdignore')) # Keeps Godot from importing the build output.
published = File.join(build, 'corelib', 'publish')
output = IO.popen(['dotnet', 'publish', File.join(__dir__, 'CoreLib', 'NctrlCore.csproj'), '-c', 'Release', '-r', 'ios-arm64', '-o', published], err: [:child, :out], &:read)
abort("Building NctrlCore failed:\n#{output.lines.last(15).join}") unless $?.success?
# A framework with its debug symbols, packaged as an xcframework so Xcode puts the dSYM in the archive (App Store
# Connect warns "Upload Symbols Failed" without it).
staging = File.join(build, 'corelib', 'framework')
FileUtils.rm_rf(staging)
framework = File.join(staging, 'NctrlCore.framework')
FileUtils.mkdir_p(framework)
FileUtils.cp(File.join(published, 'NctrlCore.dylib'), File.join(framework, 'NctrlCore'))
Xcodeproj::Plist.write_to_path({
  'CFBundleExecutable' => 'NctrlCore', 'CFBundleIdentifier' => "#{bundle}.core", 'CFBundleName' => 'NctrlCore',
  'CFBundlePackageType' => 'FMWK', 'CFBundleInfoDictionaryVersion' => '6.0', 'CFBundleSupportedPlatforms' => ['iPhoneOS'],
  'CFBundleShortVersionString' => release.build_settings['MARKETING_VERSION'] || '1.0',
  'CFBundleVersion' => release.build_settings['CURRENT_PROJECT_VERSION'] || '1', 'MinimumOSVersion' => '17.0'
}, File.join(framework, 'Info.plist'))
symbols = File.join(staging, 'NctrlCore.framework.dSYM')
FileUtils.mkdir_p(File.join(symbols, 'Contents', 'Resources', 'DWARF'))
FileUtils.cp(File.join(published, 'NctrlCore.dylib.dsym', 'Contents', 'Resources', 'DWARF', 'NctrlCore.dylib'),
             File.join(symbols, 'Contents', 'Resources', 'DWARF', 'NctrlCore'))
dsym_info = Xcodeproj::Plist.read_from_path(File.join(published, 'NctrlCore.dylib.dsym', 'Contents', 'Info.plist'))
dsym_info['CFBundleIdentifier'] = 'com.apple.xcode.dsym.NctrlCore'
Xcodeproj::Plist.write_to_path(dsym_info, File.join(symbols, 'Contents', 'Info.plist'))
FileUtils.rm_rf(File.join(export, 'NctrlCore.framework')) # From earlier versions of this script.
xcframework = File.join(export, 'NctrlCore.xcframework')
FileUtils.rm_rf(xcframework)
output = IO.popen(['xcodebuild', '-create-xcframework', '-framework', framework, '-debug-symbols', symbols, '-output', xcframework], err: [:child, :out], &:read)
abort("Packaging NctrlCore failed:\n#{output}") unless $?.success?
core = project.main_group.new_reference('NctrlCore.xcframework')
core.last_known_file_type = 'wrapper.xcframework'
embed_frameworks = app.copy_files_build_phases.find { |p| p.name == 'Embed Frameworks' } || app.new_copy_files_build_phase('Embed Frameworks')
embed_frameworks.dst_subfolder_spec = '10' # Frameworks
embed_frameworks.add_file_reference(core, true).settings = { 'ATTRIBUTES' => %w[CodeSignOnCopy RemoveHeadersOnCopy] }

# The app joins the App Group.
entitlements_path = File.join(export, release.build_settings['CODE_SIGN_ENTITLEMENTS'] || 'nCTRL/nCTRL.entitlements')
entitlements = File.exist?(entitlements_path) ? Xcodeproj::Plist.read_from_path(entitlements_path) : {}
entitlements['com.apple.security.application-groups'] = [app_group]
Xcodeproj::Plist.write_to_path(entitlements, entitlements_path)

attributes = project.root_object.attributes['TargetAttributes'] ||= {}
app_attributes = attributes[app.uuid] || {}
embed = app.copy_files_build_phases.find { |p| p.name == 'Embed Foundation Extensions' } ||
        app.new_copy_files_build_phase('Embed Foundation Extensions')
embed.dst_subfolder_spec = '13' # PlugIns

EXTENSIONS.each do |x|
  folder = File.join(export, x[:name])
  FileUtils.rm_rf(folder)
  FileUtils.mkdir_p(folder)
  (Dir[File.join(source, x[:folder], '*')] + Dir[File.join(source, 'Shared', '*.swift')]).each { |f| FileUtils.cp(f, folder) }
  Xcodeproj::Plist.write_to_path({ 'com.apple.security.application-groups' => [app_group] }, File.join(folder, "#{x[:name]}.entitlements"))

  target = project.new_target(:app_extension, x[:name], :ios, '17.0', nil, :swift)
  group = project.main_group.new_group(x[:name], x[:name])
  Dir[File.join(folder, '*.swift')].sort.each { |f| target.add_file_references([group.new_reference(File.basename(f))]) }
  group.new_reference('Info.plist')
  group.new_reference("#{x[:name]}.entitlements")
  Dir[File.join(folder, '*.h')].each { |f| group.new_reference(File.basename(f)) }
  target.frameworks_build_phase.add_file_reference(core) if x[:name] == 'nCTRLShare'
  target.build_configurations.each do |config|
    from = (app.build_configurations.find { |c| c.name == config.name } || release).build_settings
    settings = config.build_settings
    %w[DEVELOPMENT_TEAM CODE_SIGN_STYLE CODE_SIGN_IDENTITY MARKETING_VERSION CURRENT_PROJECT_VERSION TARGETED_DEVICE_FAMILY].each do |key|
      settings[key] = from[key] if from[key]
    end
    settings['PRODUCT_BUNDLE_IDENTIFIER'] = "#{bundle}.#{x[:suffix]}"
    settings['PRODUCT_NAME'] = '$(TARGET_NAME)'
    settings['INFOPLIST_FILE'] = "#{x[:name]}/Info.plist"
    settings['GENERATE_INFOPLIST_FILE'] = 'NO'
    settings['CODE_SIGN_ENTITLEMENTS'] = "#{x[:name]}/#{x[:name]}.entitlements"
    settings['SWIFT_VERSION'] = '5.0'
    settings['IPHONEOS_DEPLOYMENT_TARGET'] = '17.0'
    settings['SKIP_INSTALL'] = 'YES'
    settings['APPLICATION_EXTENSION_API_ONLY'] = 'YES'
    settings['LD_RUNPATH_SEARCH_PATHS'] = ['$(inherited)', '@executable_path/Frameworks', '@executable_path/../../Frameworks']
    if x[:name] == 'nCTRLShare'
      settings['FRAMEWORK_SEARCH_PATHS'] = ['$(inherited)', '$(PROJECT_DIR)']
      settings['SWIFT_OBJC_BRIDGING_HEADER'] = 'nCTRLShare/NctrlCore.h'
    end
  end
  attributes[target.uuid] = { 'CreatedOnToolsVersion' => '15.0', 'ProvisioningStyle' => 'Automatic' }
  attributes[target.uuid]['DevelopmentTeam'] = app_attributes['DevelopmentTeam'] if app_attributes['DevelopmentTeam']
  app.add_dependency(target)
  embed.add_file_reference(target.product_reference, true).settings = { 'ATTRIBUTES' => ['RemoveHeadersOnCopy'] }
end

project.save
puts "Added the widget, the Share extension and NctrlCore (App Group #{app_group.sub(bundle, '<app bundle ID>')})."
