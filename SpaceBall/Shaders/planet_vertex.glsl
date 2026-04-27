#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;

out vec3 FragPos;
out vec3 Normal;
out float Height;
out vec2 TexCoord;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform sampler2D heightmap1;
uniform sampler2D heightmap2;
uniform float blendFactor;
uniform float radius;
uniform float dispScale;

void main()
{
    vec2 uv = vec2(aUV.x, 1.0 - aUV.y);
    float h1 = texture(heightmap1, uv).r * 2.0 - 1.0;
    float h2 = texture(heightmap2, uv).r * 2.0 - 1.0;
    float h = mix(h1, h2, clamp(blendFactor, 0.0, 1.0));
    Height = h;
    TexCoord = aUV;
    float disp = h * dispScale;
    vec3 pos = aPos + aNormal * disp;
    vec4 worldPos = model * vec4(pos, 1.0);
    FragPos = worldPos.xyz;
    Normal = mat3(transpose(inverse(model))) * aNormal;
    gl_Position = projection * view * worldPos;
}
