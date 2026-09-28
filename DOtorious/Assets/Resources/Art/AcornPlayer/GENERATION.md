# Acorn player artwork

Generated with the built-in imagegen tool on 2026-09-27. Original PNG pixels are preserved; Unity Sprite Importer slices frames and assigns aligned pivots. No external API key or CLI was used.

## Character motion atlas

Output: AcornMotions.png

Prompt:

Use case: stylized-concept. Asset type: production 2D side-scrolling pixel-art character ANIMATION SPRITE SHEET, transparent RGBA background. Create an original living acorn hero: the entire round oval torso/head is a golden-brown acorn with a dark textured brown cup cap and short stem, expressive small dark eyes and a tiny mouth on the acorn, two articulated brown twig arms ending in wooden mitten-like hands, two short sturdy brown twig legs/feet. NOT a human wearing an acorn hat. No clothes, no weapon, no extra accessory. Muted warm wood palette, dark pixel outline, careful clustered pixel shading with a polished gothic-fantasy metroidvania sprite feel; readable silhouette, not smooth 3D/vector art. All sprites face RIGHT in a consistent near-profile 2D view. Deliver ONE tall sheet, EXACTLY SIX equally spaced columns and TEN equally spaced rows = 60 separate full-body frames. Each row is a complete six-frame animation in order left to right. All cells identical size, same camera, body scale and foot baseline; keep torso centered at same x in its cell, feet at 85% cell height (except airborne/knocked down motion), margins transparent, no frame touches another. Character occupies about 60% of cell height and 55% of cell width so hands fit. Rows top-to-bottom: (1) idle breathing/blinking loop, (2) walking loop with alternating feet and arms, (3) running loop with distinct stride and forward lean, (4) jump anticipation crouch, takeoff, rise, apex, fall, landing, (5) LIGHT PUNCH windup then quick right-hand jab then recover, (6) HEAVY ATTACK both wooden arms wind up and a powerful forward two-fist strike then recover, (7) WOOD SPEAR CAST open right palm forward, a short brown twig buds from palm then thrusts, then recover (only SHORT branch at palm; long extending spear effect is a separate game object, do not draw long VFX here), (8) ACORN THROW windup holding one small acorn in hand, arm throw forward, hand release, follow-through, recovery (no flying projectile outside hand), (9) DASH crouch, lean-forward surge, horizontal lunging pose, surge, brake, recover with no baked-in motion trail, (10) HURT/DEATH flinch, recoil, stagger, fall, lying on side, still on side. Same identifiable acorn face, cap, limb colors and size across all 60 frames. This is an animation atlas, not a collage. No text, no row labels, no grid lines, no ground shadow, no scenery, no decorative border, no watermark. Genuinely transparent empty background, not a checkerboard painted into pixels. Tall resolution suited to a 6 columns by 10 rows grid.

## Growing wood thrust

Output: WoodThrust.png

Prompt:

Use case: stylized-concept. Asset type: transparent 2D pixel-art WOOD SPEAR GROWTH effect sprite sheet for a living brown acorn hero. ONE sheet, EXACTLY 3 columns by 2 rows = six chronological frames, every cell same rectangle twice as wide as tall. A brown wooden root shoots horizontally to the RIGHT, bark-colored warm medium walnut brown, dark pixel outline, golden brown bark shading. No character, no arm, no ground. Every frame's branch begins at the SAME left-middle origin of its cell, grows in length to right; frame1 tiny pointed wooden bud, frame2 short branch, frame3 half-extended, frame4 long sharp wooden lance, frame5 fully extended jagged sharp branch with small branching thorns (mostly straight horizontal), frame6 fading splinters after retraction. The branch color is brown wood, NOT green energy, NOT fire. Crisp pixel clusters, limited warm dark-fantasy palette, readable silhouette. Pad every cell generously, no overlap, no borders, no numbers, no text. Genuinely transparent RGBA empty background, no baked checkerboard. 1536x1024 or landscape canvas with perfect 3x2 grid. This is a VFX animation asset, not concept art.

## Thrown acorn

Output: AcornProjectile.png

Prompt:

Create a production-ready transparent-background pixel-art VFX spritesheet for a dark fantasy side scrolling 2D game: a thrown golden-brown acorn, dark chocolate woody cap and short stem. NO face or limbs, just a small acorn projectile. EXACTLY 6 distinct frames in a single horizontal row, evenly spaced nonoverlapping equal-size cells, object centered in each cell with generous transparent padding. Frames show a complete tumbling rotation from upright through diagonal, sideways, upside-down and back. Identical acorn size, clustered crisp pixels, limited warm brown, ochre and amber palette, softly lit edges but no bloom, no antialias blur, no floor or cast shadow, no background, no text, no guides or grid. High-quality game asset with truly transparent alpha.


