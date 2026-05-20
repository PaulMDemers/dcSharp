# Graphics Bring-Up

## Graphics Milestones

A useful graphics ladder:

1. Decode display/GPU commands.
2. Clear a framebuffer.
3. Dump a candidate frame.
4. Draw a point or line.
5. Draw a flat triangle.
6. Draw colored triangles.
7. Sample a texture.
8. Support depth.
9. Support blending and alpha.
10. Support render targets.
11. Support shaders/combiners or fixed-function equivalents.
12. Present frames through the frontend.

Do not skip the early milestones. "The screen is noisy" and "the screen is
flat" are different failures.

## Count More Than Primitives

Primitive counters are not enough. Track:

- Clears.
- Draw commands.
- Primitive assembly count.
- Rasterized fragment hit/miss.
- Depth-test failures.
- Alpha-test failures.
- Color-write disabled cases.
- Texture sample success/failure.
- Frame nonzero pixels.
- Unique colors.
- Frame fingerprint.

In this project, several paths counted triangles but produced no rasterized
fragments. Fragment probes made that visible.

## Synthetic Renderer Paths

For HLE or intermediate graphics bring-up, a software renderer can accept
synthetic primitives. Keep these paths explicit:

- Object-space vertex submission.
- Screen-space vertex submission.
- Synthetic texture registration.
- Synthetic color fallback.
- Synthetic primitive mode.

This lets HLE convert guest-staged data into visible output while the lower-level
GPU path is still incomplete.

## Projection Lessons

When geometry disappears:

- Check whether vertices are object, view, clip, normalized-device, or screen
  coordinates.
- Check whether the guest has already transformed them.
- Check `w`, depth, near/far mapping, and viewport state.
- Check culling orientation.
- Check whether coordinates are finite.

One major win came from recognizing that staged vertices were already in a
post-transform coordinate space and should be projected to screen directly
instead of being sent through the normal object-space path again.

## Alpha And Color

Blank frames often come from alpha, not position.

Check:

- Vertex alpha.
- Texture alpha.
- Alpha test state.
- Blend state.
- Color mask.
- Clear color.
- Packed versus float color formats.

For synthetic paths, an RGB color with alpha zero may need an opaque fallback if
the guest's real pipeline would have supplied alpha elsewhere.

## Texture Bring-Up

Texture failures should report:

- Stage enabled/disabled.
- Shader/combiner mode.
- Texture base address.
- Format.
- Width, height, pitch.
- Swizzle/linear mode.
- Mip level.
- Addressing mode.
- Sample coordinates before and after projection.
- Sampled texel address.

If textured tests become flat-colored after a projection or color fix, the next
blocker is probably state binding, texture coordinate decoding, or combiner
mode.

## Frame Sources

Different frame sources answer different questions:

- **Renderer snapshot:** what the emulator's renderer believes it drew.
- **Guest framebuffer memory:** what software/device writes produced.
- **Write hotspot candidate:** where guest writes resemble a framebuffer.
- **Presented frame:** what the real display path exposed.

Record the source in every artifact. A visible memory candidate with an empty
renderer snapshot points to a different subsystem than a renderer snapshot that
never reaches presentation.

## Screenshots Need Metrics

Automated graphics work needs numbers:

- Nonzero pixel count.
- Unique color count.
- Coherence or entropy score.
- Fingerprint hash.
- Fragment probe summary.

These metrics let sweeps detect "flat black," "flat clear color," "noise," and
"meaningful geometry" without manually opening every image.
