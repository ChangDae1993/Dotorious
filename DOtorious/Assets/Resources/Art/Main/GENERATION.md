# Winter Path — image-generation prompts

All artwork was produced with the built-in ImageGen tool. Original outputs are retained in the Codex generated_images directory. Assets are sliced in Unity; source bitmaps are not destructively cropped.

## CrawlerSheet.png

Use case: stylized-concept
Asset type: production 2D side-scrolling winter game animated enemy sprite sheet.
Subject: one compact Frost Crawler automaton, squat iron-and-brass beetle robot with snow crust, four short articulated feet, a cyan glowing round eye looking RIGHT, ice-blue exhaust. Premium readable pixel art, dark outlines, muted blue-gray metal and brass, warm amber tiny accents, matches moody snowy forest platform game.
Layout: square 1024x1024 RGBA PNG, exactly 4 columns by 4 rows, 16 equal 256x256 cells, NO grid lines. Every cell contains this SAME creature at SAME scale, whole body visible, centered x128, ground/feet baseline y210 inside each cell, max visible width200 and height170. Leave margins; nothing crosses cell edges.
Animation sequence from left to right: row1 four distinct subtle idle eye/breath/steam frames; row2 four clearly different alternating foot walk poses; row3 four attack poses, crouched anticipation then foreleg extended swipe to right, cyan swipe, recovery; row4 flinch, damaged stagger, breaking into harmless machine pieces, small icy debris pile. No gore.
Background: genuinely transparent alpha outside sprites, not checkerboard or colored background. Pixel edges crisp, consistent camera side view; no labels, text, numbers, cast ground shadows, unrelated objects, watermark.

## HareSheet.png

Use case: stylized-concept
Asset type: 2D winter platformer animated enemy sprite sheet.
Primary request: ONE consistent little hostile clockwork snow hare with white shaggy fur over blue steel armor, long ice-crystal ears, amber eye, tiny brass joints; faces RIGHT. Premium crisp pixel art suitable for moody snow forest action game.
Composition: square transparent RGBA sheet, exactly FOUR columns by FOUR rows of equal cells. 16 images of the SAME character, constant size/identity. Keep each full sprite well inside its own cell, 18% transparent safety margins. Centered horizontally and same foot baseline in every cell. No crossing cell borders.
Row 1 four idle breathing/ear-twitch poses; row 2 four RUN/HOP poses with clear extended then tucked legs; row 3 four charge attack anticipation-lunge-strike-recover poses toward right; row 4 hit recoil, dizzy pose, burst into small snow crystals, a low harmless melting snow pile. All rows uniform cell height, animation ordered left to right. Character occupies only about 65% of each cell.
Style: detailed but readable game pixel art, thick dark blue outline, cool white/cyan palette and small warm eye. True transparent alpha outside, no gridlines, no text, no labels, no solid backdrop, no checkerboard, no watermark, no gore.

## LanternSheet.png

Use case: stylized-concept
Asset type: animated enemy sprite sheet for 2D side-scrolling snowy forest game.
Subject: ONE same small hovering Frost Lantern Sentinel in every frame: antique dark brass cage with a pointed snow-covered hood, luminous cyan crystal eye facing RIGHT, two floating ice shards, translucent blue ghost flame beneath. Distinct readable silhouette, polished crisp pixel art.
Layout: square RGBA sprite sheet with exactly 4 columns x4 rows of equal cells, 16 centered full-object frames, each sprite fills about 65% of its cell, same scale and central origin, generous transparent margins and no crossing cell edges. Row1 four idle gently pulsing flame poses; row2 four hovering/twisting movement poses; row3 four ranged attack poses: crystal charges, bright anticipation, ejects a small blue ice bolt to right still within cell, dims/recovery; row4 hit recoil, cracked crystal, dispersing icy shards, faint dissipating flame. Every row has four frames, equally spaced, no extra frames.
Color: dark blue outlines, icy cyan, pale silver highlights, aged bronze small accents. Genuine transparent alpha outside each isolated sprite. No backdrop/checkerboard, no labels, grid lines, letters or watermark.

## StageProps.png

Use case: stylized-concept
Asset type: 2D side-scrolling snow platformer environment atlas.
Primary request: exact 2 columns x 2 rows, four equally spaced isolated wide game props on a square genuinely transparent RGBA canvas. Top left: long flat stone jump ledge covered in bright snow, a horizontal perfectly level walkable top, dark gray fractured rock underside and thin icicles, width 4 times height. Top right: old rope-supported wooden bridge section, horizontal level snow-covered deck, broken weathered timber and ropes beneath, side view, width 4 times height. Bottom left: small cozy winter hunter cabin, thick snowy gabled roof, dark timber wall, glowing amber window and door, smoking stone chimney, full side-front game elevation. Bottom right: ruined stone gateway with frosty carved pillars and faint cyan rune aperture, a complete object standing upright. All props fully visible with at least 8% transparent cell padding, same crisp detailed pixel art style as atmospheric snowy forest game, muted midnight blue/gray with warm lantern windows. No words, no labels, no grid borders, no visible checkerboard/solid background, no logos.

## ForestTrees.png

Use case: game-asset. Create an RGBA TRANSPARENT PNG game sprite atlas. Exactly 3 separate snowy trees lined up horizontally across a wide landscape canvas, complete roots and tops, no text. Left full tall blue spruce covered in fresh snow; middle ancient twisted bare dark oak with snow on branches; right crooked sparse fir covered in icicles. Premium hand-painted pixel art, cool navy steel blue shadows, ivory snow, side view, detailed but crisp, winter metroidvania. Every tree isolated with generous empty padding around and between, similar height, no floor, no gradient, no scenery, no shadow outside objects. Background MUST be empty with true alpha transparency. Do not draw a checkerboard or a flat background.

