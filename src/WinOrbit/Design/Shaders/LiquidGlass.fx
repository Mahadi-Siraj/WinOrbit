// Curved glass edge for WinOrbit's glass surfaces (see LiquidGlassEffect.cs).
//
// It runs on the backdrop layer only — the blurred wallpaper slice — never on text or icons. Deep inside the glass
// the wallpaper passes through untouched; within a thin band at the rim it is pulled inward along the edge's
// normal, more strongly the closer to the edge, so the background bends round the curve like thick polished glass.
// Red, green and blue are bent by slightly different amounts there (a faint prism), and the side of the rim that
// faces the pointer catches a soft highlight while the pointer is over the glass.
//
// Pixel shader 2.0, so WPF can also run it without a graphics card.
// Rebuild after editing:  .\tools\compile-shader.ps1 src\WinOrbit\Design\Shaders\LiquidGlass.fx

sampler2D input : register(s0);
float4 shape : register(c0);   // x, y: size in pixels; z: depth of the bending band in pixels; w: how far it bends, in pixels
float4 radii : register(c1);   // corner radii in pixels: top-left, top-right, bottom-right, bottom-left
float4 light : register(c2);   // x, y: unit direction from the centre towards the pointer; z: highlight strength; w: colour split
float4 rim : register(c3);     // x: how bright the rim's own sheen is (thick glass catches light all round its edge)

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 size = shape.xy;
    float2 p = (uv - 0.5) * size;           // from the centre, in pixels
    float2 side = step(0, p) * 2 - 1;       // which quadrant: -1 or +1 on each axis

    // This quadrant's corner radius.
    float right = step(0, p.x);
    float r = lerp(lerp(radii.x, radii.y, right), lerp(radii.w, radii.z, right), step(0, p.y));

    // Signed distance to the rounded rectangle: negative inside, 0 on the edge.
    float2 q = abs(p) - size * 0.5 + r;
    float2 m = max(q, 0);
    float d = length(m) + min(max(q.x, q.y), 0) - r;

    // Outward direction of the nearest edge: round in the corners, straight along the sides.
    float a = step(q.y, q.x);
    float2 n = lerp(float2(a, 1 - a), m, step(0.001, m.x + m.y));
    n = normalize(n + 0.00001) * side;

    // Bending: nothing deep inside, strongest right at the rim (a curved lens profile).
    float t = saturate(1 + d / shape.z);
    float lens = t * t;
    float2 bend = -n * lens * shape.w / size;

    float4 here = tex2D(input, uv);
    float red = tex2D(input, uv + bend * (1 + light.w)).r;
    float4 mid = tex2D(input, uv + bend);
    float blue = tex2D(input, uv + bend * (1 - light.w)).b;
    float3 rgb = float3(red, mid.g, blue);

    // Highlight on the part of the rim facing the pointer, plus a faint sheen all round the rim so the glass
    // reads as thick even where there is little behind it to bend.
    float facing = saturate(dot(n, light.xy));
    rgb += lens * (facing * facing * facing * light.z * 0.32 + lens * lens * rim.x * 0.09);

    // Keep the surface's own outline (its anti-aliased edge); the samples above come from just inside it.
    return float4(rgb * here.a, here.a);
}
