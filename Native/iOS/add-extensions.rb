#!/usr/bin/env ruby
# Adds nCTRL's widget and its "Send to car" Share extension to the Xcode project Godot exports, with an App Group
# shared by the app and both extensions ("group.<app bundle ID>"). Godot regenerates the project on every export,
# so run this after each one:
#
#   ruby Native/iOS/add-extensions.rb <folder that holds nCTRL.xcodeproj>
#
# Needs the xcodeproj gem. Team, bundle ID and version come from the exported app target; nothing personal is
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
  end
  attributes[target.uuid] = { 'CreatedOnToolsVersion' => '15.0', 'ProvisioningStyle' => 'Automatic' }
  attributes[target.uuid]['DevelopmentTeam'] = app_attributes['DevelopmentTeam'] if app_attributes['DevelopmentTeam']
  app.add_dependency(target)
  embed.add_file_reference(target.product_reference, true).settings = { 'ATTRIBUTES' => ['RemoveHeadersOnCopy'] }
end

project.save
puts "Added the widget and Share extension (App Group #{app_group.sub(bundle, '<app bundle ID>')})."
