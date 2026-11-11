# Art notes

Original AI-generated pixel-style Ioniq 5 N illustration, created for this companion-app prototype. Front three-quarter view; pale performance blue body, red lower trim, black wheels, dark glass, square headlights and a dark navy backdrop. Recolored at runtime with `Paint.gdshader`.

The artwork is illustrative and may differ from exact production bodywork. Hyundai, Ioniq and N names identify the vehicle; this is not an official Hyundai product. Maker badges (the Hyundai, N, Genesis and Kia logos on grilles, hoods and wheel centers) have been painted out of every car picture and the app icon, so the art shows no trademarks. UI icons are drawn in code. DejaVu Sans Regular and Bold are bundled with their license.

Direction: selected concept 02 / N Performance. Palette: #08121d background, #8ecbfa primary accent, #f24a4d red detail, #eef5fb primary text, #9ab0c6 secondary text. Angular panels, horizontal pixel charge bars and a large vehicle preview carry the styling. Paint choice affects the car, not the UI accent palette.

gtCTRL icon (`Assets/gtctrl_icon.png`): the EV6 GT picture repainted gray by `Tools/Icon/make_icon.py` with the same body selection as `Paint.gdshader`, on the GT Neon graphite with a pixel "GT" mark drawn in code. Kia and EV6 GT names identify the vehicle; this is not an official Kia product, and no Kia logo is used. The same script makes the paint-matched alternate icons (`Native/iOS/AppIcons/AppIcon-*.png`), the grayscale Tinted icon (`Tinted.png`) and the launch splash (`Assets/gtctrl_splash.png`).

EV6 GT wheels: the wheels on `Assets/kia_ev6_gt_pixel.png` were redrawn as the GT's 21-inch wheels by `Tools/Art/ev6gt_wheels.py`, which reduces a straight-on wheel photo to a few flat colors and fits it to each wheel's oval (the photo itself isn't in the repo). The wheel center logo is blacked out.
