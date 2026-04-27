#version 330 core
out vec4 FragColor;

in vec3 Normal;
in vec3 FragPos;
in vec2 TexCoord;
in float Height;

uniform vec3 lightPos;
uniform vec3 lightColor;
uniform float atmosphere;
uniform float temperature;
uniform float geologicActivity;
uniform float time;
uniform vec3 cameraPos;
uniform float cloudDensity;
uniform int seed;
uniform float radius;

// Simple hash function for noise
float hash(vec3 p) {
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// 3D noise
float noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    
    return mix(
        mix(mix(hash(i + vec3(0,0,0)), hash(i + vec3(1,0,0)), f.x),
            mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
        mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x),
            mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
}

// Fractal Brownian Motion, 4 octaves (constant for driver compatibility)
float fbm4(vec3 p) {
    float v = 0.0, amp = 0.5, freq = 1.0;
    v += amp * noise(p * freq); amp *= 0.5; freq *= 2.0;
    v += amp * noise(p * freq); amp *= 0.5; freq *= 2.0;
    v += amp * noise(p * freq); amp *= 0.5; freq *= 2.0;
    v += amp * noise(p * freq);
    return v;
}

// Color by height and temperature, smooth blending to avoid banding
float blendBand(float h, float lo, float hi) {
    return smoothstep(lo, hi, h);
}
vec3 colorForHeight(float h, float temp) {
    float t;
    vec3 a, b;
    if (temp < 0.3) {
        if (h < -0.05) return mix(vec3(0.1, 0.2, 0.4), vec3(0.7, 0.8, 0.9), blendBand(h, -0.2, -0.05));
        if (h < 0.2) return mix(vec3(0.7, 0.8, 0.9), vec3(0.9, 0.95, 1.0), blendBand(h, -0.05, 0.2));
        if (h < 0.5) return mix(vec3(0.9, 0.95, 1.0), vec3(0.85, 0.9, 0.95), blendBand(h, 0.2, 0.5));
        return mix(vec3(0.85, 0.9, 0.95), vec3(1.0, 1.0, 1.0), blendBand(h, 0.5, 0.7));
    }
    if (temp > 0.7) {
        if (h < -0.05) return mix(vec3(0.8, 0.2, 0.0), vec3(0.3, 0.1, 0.05), blendBand(h, -0.2, -0.05));
        if (h < 0.2) return mix(vec3(0.3, 0.1, 0.05), vec3(0.6, 0.3, 0.1), blendBand(h, -0.05, 0.2));
        if (h < 0.5) return mix(vec3(0.6, 0.3, 0.1), vec3(0.5, 0.25, 0.1), blendBand(h, 0.2, 0.5));
        return mix(vec3(0.5, 0.25, 0.1), vec3(1.0, 0.4, 0.1), blendBand(h, 0.5, 0.7));
    }
    if (h < -0.2) return mix(vec3(0.0, 0.1, 0.3), vec3(0.0, 0.2, 0.5), blendBand(h, -0.35, -0.2));
    if (h < 0.0) return mix(vec3(0.0, 0.2, 0.5), vec3(0.76, 0.7, 0.5), blendBand(h, -0.2, 0.0));
    if (h < 0.3) return mix(vec3(0.76, 0.7, 0.5), vec3(0.2, 0.5, 0.1), blendBand(h, 0.0, 0.3));
    if (h < 0.5) return mix(vec3(0.2, 0.5, 0.1), vec3(0.1, 0.35, 0.05), blendBand(h, 0.3, 0.5));
    if (h < 0.75) return mix(vec3(0.1, 0.35, 0.05), vec3(0.4, 0.35, 0.3), blendBand(h, 0.5, 0.75));
    return mix(vec3(0.4, 0.35, 0.3), vec3(0.95, 0.95, 1.0), blendBand(h, 0.75, 0.95));
}

void main()
{
    float ambientStrength = 0.35;
    vec3 ambient = ambientStrength * lightColor;

    vec3 norm = normalize(Normal);
    vec3 lightDir = normalize(lightPos - FragPos);
    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diff * lightColor;

    vec3 vertexColor = colorForHeight(Height, temperature);
    vec3 result = (ambient + diffuse) * vertexColor;

    vec3 viewDir = normalize(cameraPos - FragPos);
    float rim = 1.0 - max(dot(viewDir, norm), 0.0);
    rim = pow(rim, 2.0);

    float height = length(FragPos) / radius;

    float coldFogFactor = smoothstep(0.5, 0.2, temperature);
    float fogStrength = coldFogFactor * atmosphere * 1.8;

    if (fogStrength > 0.01) {
        float heightFog = smoothstep(0.0, 0.8, height);
        float totalFog = fogStrength * (0.5 + heightFog * 0.5);
        vec3 fogPos = normalize(FragPos) * 3.0 + vec3(time * 0.01);
        float fogNoise = fbm4(fogPos) * 0.3;
        totalFog = clamp(totalFog + fogNoise * fogStrength, 0.0, 0.95);
        vec3 fogColor = vec3(0.9, 0.95, 1.0);
        result = mix(result, fogColor, totalFog);
        vec3 coldRimColor = vec3(0.6, 0.8, 1.0);
        result += coldRimColor * rim * atmosphere * coldFogFactor * 1.5;
    }

    float heatFactor = smoothstep(0.5, 0.8, temperature);
    float heatStrength = heatFactor * geologicActivity * 0.8;

    if (heatStrength > 0.01) {
        float pulse = 0.7 + 0.3 * sin(time * 2.0 + height * 5.0);
        vec3 heatHaze = vec3(1.0, 0.3, 0.1) * heatStrength * pulse;
        float hazeCoverage = heatStrength * 0.8;
        result = mix(result, result * vec3(1.5, 0.5, 0.3), hazeCoverage);
        result += heatHaze * 0.5;
        if (Height < -0.1) {
            float lavaGlow = 0.5 + 0.5 * sin(time * 3.0 + Height * 20.0);
            result = mix(result, vec3(1.0, 0.4, 0.0), lavaGlow * 0.8 * heatStrength);
        }
    }

    float infernoFactor = smoothstep(0.7, 0.95, temperature) * smoothstep(1.0, 2.0, geologicActivity);

    if (infernoFactor > 0.01) {
        float infernoStrength = infernoFactor * 0.9;
        float infernoPulse = 0.6 + 0.4 * sin(time * 4.0);
        vec3 infernoColor = vec3(1.0, 0.2, 0.0) * infernoPulse;
        result = mix(result, infernoColor, infernoStrength);
        vec3 crackPos = normalize(FragPos) * 10.0;
        float cracks = fbm4(crackPos);
        cracks = smoothstep(0.4, 0.6, cracks);
        result += vec3(1.0, 0.8, 0.3) * cracks * infernoStrength;
    }

    if (geologicActivity > 0.8) {
        float volcanoGlow = smoothstep(0.5, 1.0, height) * (geologicActivity - 0.8) * 0.5;
        vec3 volcanoColor = vec3(1.0, 0.3, 0.05);
        result += volcanoColor * volcanoGlow * (0.5 + 0.5 * sin(time * 3.0 + height * 10.0));
    }

    if (atmosphere > 0.2 && temperature > 0.3 && temperature < 0.7) {
        vec3 cloudPos = normalize(FragPos) * 2.0;
        float seedOffset = float(seed % 1000) * 0.001;
        vec3 animatedPos = cloudPos + vec3(time * 0.005, 0.0, time * 0.003) + vec3(seedOffset);
        float cloudNoise = fbm4(animatedPos * 3.0);
        cloudNoise = smoothstep(0.3, 0.7, cloudNoise);
        float coverage = atmosphere * cloudDensity * 1.5;
        cloudNoise *= coverage;
        vec3 cloudColor = vec3(0.95, 0.97, 1.0);
        float cloudLight = 0.5 + 0.5 * dot(norm, lightDir);
        cloudColor *= cloudLight;
        result = mix(result, cloudColor, cloudNoise * 0.8);
    }

    vec3 coldAtm = vec3(0.3, 0.5, 1.0);
    vec3 hotAtm = vec3(1.0, 0.3, 0.1);
    vec3 atmosphereColor = mix(coldAtm, hotAtm, temperature);
    result += atmosphereColor * rim * atmosphere * 1.5;

    result = max(result, vec3(0.12, 0.14, 0.18));
    FragColor = vec4(result, 1.0);
}
