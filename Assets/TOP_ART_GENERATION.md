# Top-down vehicle art

Generated with the built-in image generation tool on 2026-10-24 using each corresponding original PNG in this folder as a vehicle/style reference. Original art is preserved.

All 14 delivered PNGs are exactly 66 x 160 pixels, with true alpha transparency, front facing up. Generated artwork was fitted proportionally within a 62 x 156 box on the final canvas using nearest-neighbor sampling and transparent margins. Body-blue pixels are compatible with the existing Paint.gdshader selection formula. Offline pixel checks verified dimensions, transparent outer edges, visible car pixels, and substantial paint selection for every sprite. Final sprite sheet was visually inspected. In-app/device rendering was not tested.

Use nearest texture filtering when displaying above native size. Apply Paint.gdshader to recolor blue body paint while keeping dark glass/trim and red/lime accents. These are stylized illustrations, not dimensionally exact vehicle plans.

## Shared prompt

Use case: stylized-concept. Create one transparent pixel-art vehicle sprite for nCTRL. The attached car image is ONLY a reference for the exact vehicle identity, pale-blue paint palette and pixel-art aesthetic. Completely change camera and composition to a STRICT ORTHOGRAPHIC TOP-DOWN PLAN VIEW: camera directly vertically above the center of the roof, viewing the roof, hood, windshield, rear glass and trunk; front bumper at TOP, rear bumper at BOTTOM, car aligned vertically, perfectly straight, no rotation, no perspective or isometric tilt, no visible front grille face or vertical door faces. Requested final sprite is 66 pixels WIDE by 160 pixels TALL. Design for this tiny pixel grid: bold readable clusters, crisp stair-step edges, simplified mechanical details, no fine noise. Narrow portrait canvas matching aspect 66:160. One complete car centered including both mirrors, wheels only as narrow side strips where genuinely visible overhead, and spoiler, with about 2 final pixels of clear margin. Actual transparent RGBA background, fully empty outside the car: NO colored backdrop, NO checkerboard drawn into image, NO floor, NO ground shadow or glow. Palette: tintable painted body/hood/roof is LIGHT BLUE approximately #89b7f0, highlights #b5d5ff and blue shading #557fb4. Preserve blue hue throughout painted body. Glass is dark neutral graphite #18202a, wheels black, lights neutral white/red, no bright blue glass reflections. Roof stays body-blue unless model explicitly has contrasting black roof. Black trim, white lamps and red/lime performance accents must be visually separate from blue body so a blue-selective paint shader can recolor only bodywork. Maintain model proportions, no cartoon face. No captions, labels, badges with illegible text, drop shadows or watermarks. Subject details: 

## Per-vehicle prompt suffixes and output files

### ioniq_pixel_top.png

Hyundai IONIQ 5 N. Wide angular hot crossover/hatchback, sharply faceted hood, rectangular pixel headlights and pixel rear lights, black roof spoiler, red N front splitter and side sills.

### hyundai_ioniq_5_pixel_top.png

Hyundai IONIQ 5. Standard angular retro electric hatchback/crossover, creased clamshell hood, broad squared roof, silver lower trim, rectangular pixel lamps. No N red trim or racing wing.

### hyundai_ioniq_6_n_pixel_top.png

Hyundai IONIQ 6 N. Low elongated rounded streamliner four-door sedan, long aerodynamic teardrop roof and tapered rear deck, prominent black factory rear wing near BOTTOM of sprite, red N front and side accents. Not a hatchback.

### hyundai_ioniq_6_pixel_top.png

Hyundai IONIQ 6. Standard long smooth streamliner sedan, rounded teardrop roof flowing into rear deck, original 2023-25 swept teardrop headlights, subtle integrated rear lip. No red N trim, no large rear wing.

### kia_ev6_pixel_top.png

Kia EV6. Sleek curved fastback crossover, long swept windshield, swept boomerang headlamps at TOP, muscular rear haunches and wraparound rear light at BOTTOM. Standard trim, no red N accents or big wing.

### kia_ev6_gt_pixel_top.png

Kia EV6 GT. Performance fastback crossover, same curved body as EV6 but broader sportier bumper shapes, black performance wheels with small lime-green brake details if visible overhead, dark integrated rear roof spoiler. No Hyundai red N stripe or N logo.

### kia_ev9_pixel_top.png

Kia EV9. Large long three-row boxy upright SUV, very rectangular roof/body outline, black longitudinal roof rails, vertical front corner headlights at TOP, squared tailgate at BOTTOM. No N red trim or performance wing. Blue-painted roof panels, dark windshield and rear window.

### hyundai_genesis_pixel_top.png

2015–2016 Hyundai Genesis sedan. Long executive sedan, long hood with traditional upright chrome hexagonal grille at TOP edge, swept headlights, separate rear trunk deck at BOTTOM, chrome window surrounds. No racing wing or red N accents. Hyundai-badged DH sedan, not Genesis Coupe or modern G80.

### hyundai_elantra_n_pixel_top.png

Hyundai Elantra N. Long sharply faceted sport sedan with angular hood, very thin horizontal facelift headlights at TOP, triangular body creases, distinct rear trunk and modest black factory pedestal spoiler at BOTTOM. Red N front and sill accents.

### hyundai_kona_n_pixel_top.png

Hyundai Kona N. Short tall muscular small SUV/crossover, roof rails, short hood with three small slots ahead of it, split front lamps, chunky fender outline, compact black rear roof spoiler at BOTTOM. Red N trim. Original 2022–23 Kona, not second-generation full light bar.

### hyundai_veloster_n_pixel_top.png

Hyundai Veloster N. Short compact asymmetric three-door hot hatch coupe, long single driver's door on LEFT side of overhead car, two shorter passenger doors on RIGHT, tapered coupe roof ending in short hatch, large black hexagonal front grille at TOP, black rear roof spoiler at BOTTOM, red N trim.

### hyundai_i20_n_pixel_top.png

Hyundai i20 N. Small upright short-wheelbase five-door hot hatch, angular hood/headlights, contrasting black roof (keep black roof NON-tintable), prominent small black roof spoiler at BOTTOM, red N front lip and sill trim. Body in blue.

### hyundai_i30_n_pixel_top.png

Hyundai i30 N hatchback. Conventional five-door C-segment hot hatch, smoothly sculpted hood, swept headlights and black cascading grille at TOP, blue-painted roof, upright rear hatch with black roof spoiler at BOTTOM, red N front and sill trim. Not Elantra sedan, not Fastback.

### hyundai_i30_fastback_n_pixel_top.png

Hyundai i30 Fastback N. Long flowing five-door fastback, sculpted i30 front hood/headlamps at TOP, blue-painted roof tapering into elongated sloping rear glass and rounded rear deck at BOTTOM, small black integrated trunk lip spoiler, red N trim. Longer tapered tail than i30 hatchback; no hatchback roof wing.

