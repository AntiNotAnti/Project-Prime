# Isolated primitive corpse preview

Run `dotnet run -c Release --project tools/corpse-physics-preview -- ./captures --enable-experimental-physics`.
Requires an available desktop OpenGL2.1 compatibility context. Saves54 PNGs and a
receipt for frozen/procedural/experimental physics,8 corpses,6 fixed ticks and3
interpolation fractions. The static proxy has a floor, ramp and steps. Boxes and
blue tree lines are diagnostic graphics, not native Hunter meshes or collision.
Uses its own GL window and never loads the game. No runtime screenshots or game
performance claims follow from these proxy captures. BEPU notices ship in output;
OpenTK4.9.4 is the same existing desktop graphics dependency family as the game.
