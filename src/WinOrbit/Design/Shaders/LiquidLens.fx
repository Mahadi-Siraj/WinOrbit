// Liquid glass lens for WinOrbit's "Liquid" looks (see LiquidLensEffect.cs). LiquidGlass.fx stays for the
// other themes and as the fallback where pixel shader 3.0 isn't available.
//
// The glass is modelled as a slab with a rounded bevel all round its rim. Its flat middle shows what is behind
// it almost undistorted; across the bevel the surface curves down to the edge, and light passing through it is
// bent outwards, so the rim gathers in what lies just beyond the glass and squeezes it into a narrow band. Lines in
// the background therefore bend round the corners and crowd together towards the edge, which is what makes the
// glass read as thick and liquid rather than as a frosted sticker. The input is a slightly larger picture than the
// glass (the "padding" on each side), so there is something beyond the edge to gather in.
//
// On top: a faint prism split in the bevel, light on the rim (strongest on the side facing the light, fainter on
// the opposite side, as a real rounded edge catches it), a crisp one-pixel edge line, and a readability term that
// quietly calms bright backgrounds behind dark-theme text (or dark ones behind light-theme text) in the flat middle.
//
// Pixel shader 3.0: hardware only. Rebuild after editing:
//   .\tools\compile-shader.ps1 src\WinOrbit\Design\Shaders\LiquidLens.fx -Profile ps_3_0

sampler2D input : register(s0);
float4 shape : register(c0);   // x, y: size of the glass in pixels; z: bevel width in pixels; w: unused
float4 radii : register(c1);   // corner radii in pixels: top-left, top-right, bottom-right, bottom-left
float4 light : register(c2);   // x, y: unit direction from the centre towards the light; z: extra shine (pointer, flare); w: colour split
float4 lens : register(c3);    // x: how far the rim reaches beyond the edge, px; y: flex (motion), multiplies x; z: rim light; w: readability (+ calm bright, - lift dark)
float4 pad : register(c4);     // padding the input has beyond each edge, px: left, top, right, bottom

// A point in glass pixels (0,0 = the glass's top-left corner) to a position in the padded input.
float2 Tex(float2 s)
{
    float2 full = shape.xy + pad.xy + pad.zw;
    return saturate((s + pad.xy) / full);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 size = shape.xy;
    float2 pos = uv * size;                 // glass pixels from the top-left
    float2 p = pos - size * 0.5;            // from the centre
    float2 side = step(0, p) * 2 - 1;

    // This quadrant's corner radius, and the signed distance to the rounded rectangle (negative inside).
    float right = step(0, p.x);
    float r = lerp(lerp(radii.x, radii.y, right), lerp(radii.w, radii.z, right), step(0, p.y));
    float2 q = abs(p) - size * 0.5 + r;
    float2 m = max(q, 0);
    float d = length(m) + min(max(q.x, q.y), 0) - r;

    // Outward normal of the nearest edge: round in the corners, straight along the sides.
    float a = step(q.y, q.x);
    float2 n = lerp(float2(a, 1 - a), m, step(0.001, m.x + m.y));
    n = normalize(n + 0.00001) * side;

    // Across the bevel: 1 at the very edge, 0 where it meets the flat middle.
    float bevel = max(shape.z, 1);
    float edge = 1 - saturate(-d / bevel);
    float curve = edge * edge;

    // Gather in what lies beyond the edge: the sample moves outwards, most at the rim. The mapping stays monotonic
    // (no folds) however strong the reach, because the sample point always moves inwards faster than it is pushed out.
    float reach = lens.x * max(0, 1 + lens.y);
    float2 push = n * reach * curve;
    float split = light.w * edge;
    float red = tex2D(input, Tex(pos + push * (1 + split))).r;
    float4 mid = tex2D(input, Tex(pos + push));
    float blue = tex2D(input, Tex(pos + push * (1 - split))).b;
    float3 rgb = float3(red, mid.g, blue);

    // Readability in the flat middle, where text sits: calm bright backgrounds (dark looks) or lift dark ones (light looks).
    float inner = 1 - edge;
    float lum = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    float strength = abs(lens.w) * inner;
    float3 calmed = rgb * (1 - strength * smoothstep(0.18, 1.0, lum) * 0.75);
    float3 lifted = lerp(rgb, float3(1, 1, 1), strength * (1 - smoothstep(0.0, 0.85, lum)) * 0.6);
    rgb = lerp(lifted, calmed, step(0, lens.w));

    // Light on the rounded rim: bright on the side facing the light, a softer catch on the far side, a crisp edge line.
    float facing = dot(n, light.xy);
    float rim = curve * edge;
    float lit = rim * (0.55 * pow(saturate(facing), 2) + 0.22 * pow(saturate(-facing), 2) + 0.06);
    float outline = saturate(1.0 + d / 1.4);
    lit += outline * (0.18 + 0.42 * saturate(facing) + 0.12 * saturate(-facing));
    rgb += lit * lens.z * (1 + light.z);

    // The glass's own outline, anti-aliased (the input itself is a plain rectangle).
    float alpha = saturate(0.5 - d);
    return float4(rgb * alpha, alpha);
}
