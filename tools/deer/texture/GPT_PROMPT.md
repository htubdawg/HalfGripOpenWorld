# Texture request for an image model (the deer)

## What to upload

1. **`uv_guide.png`**: the image to paint over. It shows the exact layout in flat colour blocks and nothing else. If the tool
   has an "edit image" mode, use it on this file so the layout stays put.
2. **`uv_template.png`**: the same layout with labels and the triangle wireframe, as a reference for what each island is.
3. Optional: a photo of a white-tailed deer, as a reference for colour only.

## Prompt (copy everything in the box)

```
I'm texturing a low-poly 3D game animal, a white-tailed deer (a doe, no antlers), about 1,600 triangles,
in an early-2000s PS2 style. The first image (uv_guide.png) is its UV layout: each coloured island is one
part of the body, already placed exactly where the 3D model reads it. The second image (uv_template.png)
is the same layout with labels and wireframe, for reference only.

Paint a finished, hand-painted diffuse texture over the FIRST image.

Hard rules (the texture is useless if these are broken):
- Output a square 1024x1024 image with exactly the same layout. Do not move, resize, rotate, mirror or
  redraw any island. Paint inside the islands, following their shapes.
- No text, labels, outlines or wireframe lines in the output.
- Paint a little past each island's edge in the same colour, never a hard border at the edge. Fill the
  empty space between islands with a neutral mid grey.
- Flat, even lighting: only subtle hand-painted shading, as PS2 textures did. No strong directional light
  or highlights. Short fur suggested with fine strokes, not drawn hair by hair.
- Keep the white areas white and the black areas black where the guide has them: they are the deer's
  real markings (belly, throat patch, chin, the band behind the nose, eye ring, tail underside, eyes,
  nose and hooves).

What each island is:
- BODY (big island): the trunk unrolled round its length, seen from above. The head end is at the TOP,
  the rump at the bottom. The middle stripe is the BACK (a little darker); the left half is the deer's
  left side, the right half its right side; the white at both outer edges is the BELLY.
- CHEST: the front of the chest between the front legs. RUMP: the back end, seen from behind; the white
  in its middle is the patch under the tail.
- NECK: unrolled round its length, head end at the top. The middle is the THROAT (the white block is
  the throat patch); both outer edges are the crest (the top of the neck), a little darker.
- HEAD: unrolled round its length; the NOSE end is at the BOTTOM. The middle is the forehead and top of
  the muzzle; the two black ovals are the EYES (paint a thin pale ring round each); the cheeks are either
  side; the outer white parts are the chin and the underside of the jaw. The white band near the bottom
  is the pale band behind the nose. NOSE (small island): the tip of the muzzle from the front, black.
- EAR IN (two): the inside of each ear: pale grey-brown, a darker rim, a little soft white hair. EAR OUT
  (two): the back of each ear, the coat colour, the tips darker.
- The LEGS are in three islands each (UPPER, MID, LOWER; FRONT and HIND, L and R). Each is unrolled round
  its length: the top of the leg at the top; the middle is the OUTER side, both edges the INNER side
  (white on the mid islands). The lower legs are a slightly greyer brown, darker towards the hooves.
- TAIL: its outer face, brown with a darker line down the middle and a white fringe. TAIL UNDER: its
  underside, white.
- HOOVES (tiny island): black.

Colour: summer coat, a warm reddish tan brown, darker along the back, greyer on the face and lower legs.
[Edit this line for a winter coat: grey-brown, duller and thicker.]
```

## Loading the result

A texture fits only this build of the mesh (`deer.json` beside this folder). Any change to the mesh moves the UVs, so after
rebuilding, generate the templates again (`python3 uv.py`) and paint again.
