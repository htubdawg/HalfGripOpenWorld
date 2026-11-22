# Deer model

The white-tailed deer, sculpted in Python the way `htubdawg/Claudeinoid`'s human is (see its `HANDOFF.md`): primitives as a signed
distance field with bone weights and colour regions, surface nets, region-keeping decimation to about 1,600 triangles, skin weights
(top four bones), and a split along region borders so each triangle is one flat colour (no texture).

Needs Python 3 with numpy, scipy and matplotlib. A build at the full grid takes about 90 s.

```bash
python3 deer.py 1600 0.006                                 # writes out/mesh.npz and out/deer.json
python3 preview.py out/p.png stand,graze,flag colour -90,30,150   # poses, colour | clay, yaw per column
```

- `meshtools.py`: the generic parts, copied from Claudeinoid.
- `deer.py`: the skeleton (26 bones, bind rotations zero), the primitives, regions, palettes (summer, winter) and poses.
- `uv.py`: the UV unwrap and the texture templates in `texture/` (with `GPT_PROMPT.md`, the prompt for an image model); writes
  `deer.json`, the frozen build the templates fit. Any change to the mesh means new templates and a new texture.
- `viewer/index.html`: the deer in 3D (doe, buck and fawn; poses, walk and gallop, the painted coat and others, wireframe; drop a painted texture on it to try it). One file,
  opens straight from disk; `python3 build_viewer.py` rebuilds it from `viewer/template.html`, `deer.json` and the UV guide.
- `OpenWorld/textures/deer.png`: the painted coat the game uses (the owner's, from an image model on the templates; 1024 square,
  PNG since Unity cannot read WebP). `preview_textured.png`: it on the mesh, rendered from the UVs.
- `preview_colour.png`, `preview_clay.png`: the current build (wireframe and the winter coat in the second).

- `antlers.py`: the buck's antlers (one classic eight-point set, rigid on the head bone; the buck is the doe's mesh with them
  added), written to `antlers.json`, and checked clear of the ears, neck and body in every pose; also writes the antlers' texture
  templates `texture/antlers_uv_guide.png` (paint over this) and `texture/antlers_uv_template.png` (labels, wireframe): each piece
  an upright strip, base at the bottom, tip at the top, wrapping round left to right (the seam on the inner side); both antlers
  share the strips. The painted result goes in `OpenWorld/textures/antlers.png` (1024 square, PNG); until it is there the game
  colours them with a gradient from the burr to the tips (`DeerMesh.AntlerTexture`). A fawn is the doe drawn small (no model).
- `render_herd.py`: renders as the game draws them, the painted coat and the antlers' gradient: `python3 render_herd.py out.png group`
  (a buck, a doe, a fawn, a grazing buck: `preview_herd.png`) or poses by yaws (`preview_buck.png`).
- `export_cs.py`: writes `OpenWorld/src/DeerModel.cs` (the mod ships only source) from `deer.json` (and the antlers from `antlers.json`), in the model's own frame;
  `DeerMesh` in `OpenWorld/src/Deer.cs` turns it into Unity's (x mirrored, triangles' winding reversed, a pose's Y and Z turns
  negated) and game units. Run it after any change to `deer.json`: `python3 export_cs.py`.

Metres, Y up, facing +Z, hooves on y = 0, the deer's left = +X; in the game, scaled by 3.3 to units.
