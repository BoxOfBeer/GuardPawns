using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Drawing;
using System.Drawing.Imaging;
using SpaceDNA;
using SpaceDNA.Audio;
using SpaceDNA.Core;
using System.Threading;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using ImGuiNET;

public partial class Game : GameWindow
{
    private int _sphereVao, _sphereVbo, _sphereEbo, _sphereIndexCount, _sphereProgram;
    private int _heightmapTextureA;
    private int _heightmapTextureB;
    private int _heightmapSize = 512;

    private int _starVao, _starVbo, _starCount, _starProgram;
    private int _pawnVao, _pawnVbo, _pawnProgram;
    private int _pawn3DVao, _pawn3DVbo, _pawn3DEbo, _pawn3DIndexCount, _pawn3DProgram;
    private bool _use3DPawns = true;

    private float _angle;
    private float _cameraDistance = 45f;
    private float _rotationX, _rotationY;
    private Vector2 _lastMousePos;
    
    private PlanetConfig _config = null!;
    private string _configPath = "planet_config.json";
    private KeyboardState _prevKeyboard = null!;
    
    private bool _showSliders = true;
    private bool _showMenu = false;
    private bool _showConsole = false;
    private bool _prevEsc = false;

    private float[,]? _currentHeightmap;
    private float[,]? _nextHeightmap;
    private bool _useATextureAsCurrent = true;
    private float _blendFactor = 0f;
    private bool _isTransitioning = false;
    private float _transitionTime = 0f;
    private float _transitionDuration = 2.0f;
    private Thread? _generationThread;
    private readonly object _generationLock = new object();
    private float[,]? _pendingHeightmap;
    private int _generationId = 0;
    private int _pendingGenerationId = 0;

    // Smoothly changing visual parameters (targets are in _config)
    private float _temperatureVis = 0.5f;
    private float _atmosphereVis = 0.5f;

    private int _starSeed = 12345;
    private ImGuiController? _imGuiController;
    private float _masterVolume = 0.3f;

    private PawnAgent? _pawnAgent;
    private bool _pawnsInitialized = false;
    private bool _showPawns = true;

    private PlanetAudio? _audio;
    private bool _audioEnabled = true;

    private readonly Localization _loc = new Localization();
    private Language _language = Language.Ru;

    // Creature layer: species DNA editing
    private string _speciesDnaText = DnaSequence.DefaultSequence();
    private int _pawnInitialCount = 20;
    private SpeciesBlueprint? _speciesBlueprint;
    
    // Auto-mutation
    private float _autoMutationTimer = 0f;
    
    private float _shaderTime;
    private bool _planetShaderFromFile;
    private bool _planetDiagnosticsLogged;
    private bool _pawnSkipReasonLogged;
    private bool _pawnDrawModeLogged;
    private float _pawnSurfaceAuditTimer;

    public Game(GameWindowSettings g, NativeWindowSettings n) : base(g, n) { }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        _imGuiController?.WindowResized(e.Width, e.Height);
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ProgramPointSize);

        GameLog.SetLogPath(Path.Combine(AppContext.BaseDirectory ?? ".", "spacedna.log"));
        GameLog.Log("SpaceDNA started");

        string baseDir = AppContext.BaseDirectory ?? ".";
        string dataDir = Path.GetFullPath(Path.Combine(baseDir, "data"));
        try
        {
            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);
            if (!Directory.Exists(dataDir))
            {
                dataDir = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "data"));
                if (!Directory.Exists(dataDir))
                    Directory.CreateDirectory(dataDir);
            }
            GameLog.Log($"[Data] Directory: {dataDir} (exists={Directory.Exists(dataDir)})");
        }
        catch (Exception ex)
        {
            GameLog.Log($"[Data] Failed to create data dir: {ex.Message}");
        }

        LoadLocalization();

        // Always use config next to executable so save/load use the same file (no "smooth at start" from wrong path)
        _configPath = Path.Combine(AppContext.BaseDirectory ?? ".", "planet_config.json");
        var loaded = PlanetConfig.Load(_configPath);
        if (loaded != null)
        {
            _config = loaded;
            GameLog.Log($"Config loaded: {_configPath} (Geologic={_config.GeologicActivity})");
        }
        else
        {
            _config = new PlanetConfig();
            GameLog.Log($"Config not found, using defaults. Save will create: {_configPath}");
        }
        _masterVolume = _config.Volume;
        _prevKeyboard = KeyboardState;
        
        CreateShaders();
        _heightmapTextureA = CreateHeightmapTexture(_heightmapSize);
        _heightmapTextureB = CreateHeightmapTexture(_heightmapSize);

        _temperatureVis = WorldConstants.GetEffectiveTemperature(_config.Temperature, _config.GeologicActivity);
        _atmosphereVis = WorldConstants.GetEffectiveAtmosphere(_config.Atmosphere, _config.GeologicActivity);

        // Initial generation (sync so we have something to render immediately)
        RequestPlanetRegenerate(immediate: true);
        UpdateTitle();
        
        if (_sphereVao == 0 && _config != null)
            CreateSphereGeometry();

        _imGuiController = new ImGuiController(ClientSize.X, ClientSize.Y);
        
        // Audio
        try
        {
            _audio = new PlanetAudio();
            _audio.Volume = _masterVolume;
            _audio.IsEnabled = _audioEnabled;
        }
        catch (Exception ex)
        {
            GameLog.Log($"Audio init failed: {ex.Message}");
            _audio = null;
        }
    }

    private void LoadLocalization()
    {
        string baseDir = AppContext.BaseDirectory ?? ".";
        string ru = ResolveContentPath(baseDir, "lang_ru.json");
        string en = ResolveContentPath(baseDir, "lang_en.json");
        _loc.LoadFromFiles(ru, en);
        _loc.SetLanguage(_language);
    }

    private void UpdateTitle()
    {
        Title = _loc.F("Title", _config.Temperature, _config.Atmosphere, _config.GeologicActivity, _config.NoiseFrequency);
    }

    private static string ResolveContentPath(string baseDir, string fileName)
    {
        string p1 = Path.Combine(baseDir, fileName);
        if (File.Exists(p1)) return p1;
        string p2 = Path.Combine(Environment.CurrentDirectory, fileName);
        if (File.Exists(p2)) return p2;
        return p1;
    }

    private int CurrentHeightmapTexture => _useATextureAsCurrent ? _heightmapTextureA : _heightmapTextureB;
    private int NextHeightmapTexture => _useATextureAsCurrent ? _heightmapTextureB : _heightmapTextureA;

    /// <summary>Смещение рельефа в единицах модели. Минимум ~12% радиуса, чтобы рельеф и пешки были видны.</summary>
    private float EffectiveDisplacementScale()
    {
        float baseScale = _config.DisplacementScale / _config.Density;
        float scale = baseScale * MathF.Max(1f, _config.Radius * 0.2f);
        float minDisp = _config.Radius * 0.12f;
        return MathF.Max(scale, minDisp);
    }

    private void RequestPlanetRegenerate(bool immediate = false)
    {
        var genome = _config.ToGenome();
        genome.NoiseFrequency = _config.NoiseFrequency * (_config.Radius / 5f);

        if (immediate)
        {
            // При изменении планеты: текущий становится прошлым
            if (_currentHeightmap != null)
                SaveHeightmapToDataPng(_currentHeightmap, "past.png");
            _currentHeightmap = PlanetGenerator.GenerateHeightmap(genome, _heightmapSize);
            float hmMin = float.MaxValue, hmMax = float.MinValue;
            int sz = _currentHeightmap.GetLength(0);
            for (int i = 0; i < sz; i++)
                for (int j = 0; j < sz; j++)
                {
                    float v = _currentHeightmap[i, j];
                    if (v < hmMin) hmMin = v;
                    if (v > hmMax) hmMax = v;
                }
            GameLog.Log($"[Planet] Heightmap after gen: min={hmMin:F4}, max={hmMax:F4}, range={hmMax - hmMin:F4}");
            UpdateHeightmapTexture(CurrentHeightmapTexture, _currentHeightmap);
            UpdateHeightmapTexture(NextHeightmapTexture, _currentHeightmap);
            GL.Finish();
            SaveHeightmapToDataPng(_currentHeightmap, "current.png");
            SaveHeightmapToDataPng(_currentHeightmap, "future.png");
            _blendFactor = 0f;
            _isTransitioning = false;
            CreateStars(_config.StarCount, _starSeed, _config.Radius);
            InitializeAgents(forceReset: true);
            return;
        }

        StartGenerationThread(genome);
    }

    private void StartGenerationThread(Genome genome)
    {
        int myId = Interlocked.Increment(ref _generationId);

        try
        {
            _generationThread = new Thread(() =>
                {
                    try
                    {
                    var hm = PlanetGenerator.GenerateHeightmap(genome, _heightmapSize);
                    lock (_generationLock)
                    {
                        _pendingHeightmap = hm;
                        _pendingGenerationId = myId;
                        }
                    }
                    catch (Exception ex)
                    {
                    GameLog.Log($"Generate heightmap failed: {ex.Message}");
                    }
                });
            _generationThread.IsBackground = true;
                _generationThread.Start();
            }
        catch (Exception ex)
        {
            GameLog.Log($"Generation thread start failed: {ex.Message}");
        }
    }

    private void PumpPendingGeneration(float dt)
    {
        float[,]? pending = null;
        int pendingId = 0;
        lock (_generationLock)
        {
            if (_pendingHeightmap != null)
            {
                pending = _pendingHeightmap;
                pendingId = _pendingGenerationId;
                _pendingHeightmap = null;
                _pendingGenerationId = 0;
            }
        }

        // Discard stale results (if a newer request exists)
        if (pending != null && pendingId < _generationId)
        {
            // If this is not the latest generation, ignore it
            pending = null;
        }

        if (pending != null)
        {
            // Текущий становится прошлым, пришедший — будущим
            if (_currentHeightmap != null)
                SaveHeightmapToDataPng(_currentHeightmap, "past.png");
            SaveHeightmapToDataPng(pending, "future.png");
            _nextHeightmap = pending;
            UpdateHeightmapTexture(NextHeightmapTexture, _nextHeightmap);
            _transitionTime = 0f;
            _transitionDuration = Math.Max(0.15f, _config.MutationSpeed);
            _blendFactor = 0f;
            _isTransitioning = true;
        }

        if (_isTransitioning)
        {
            _transitionTime += dt;
            _blendFactor = Math.Clamp(_transitionTime / Math.Max(0.001f, _transitionDuration), 0f, 1f);

            if (_blendFactor >= 1f)
            {
                // Finish: promote next -> current; обновляем data/
                _useATextureAsCurrent = !_useATextureAsCurrent;
                _currentHeightmap = _nextHeightmap;
                _nextHeightmap = null;
                _blendFactor = 0f;
                _isTransitioning = false;

                if (_currentHeightmap != null)
                {
                    SaveHeightmapToDataPng(_currentHeightmap, "current.png");
                    SaveHeightmapToDataPng(_currentHeightmap, "future.png");
                    _pawnAgent?.SetSurfaceState(_currentHeightmap, _nextHeightmap, _blendFactor, EffectiveDisplacementScale());
                }
            }
        }
    }

    private static float SmoothStepTo(float current, float target, float dt, float speed)
    {
        float k = 1f - MathF.Exp(-MathF.Max(0f, speed) * MathF.Max(0f, dt));
        return current + (target - current) * k;
    }

    private void CreateSphere(float radius, int latSegments, int lonSegments)
    {
        var vertices = new List<float>();
        var indices = new List<uint>();

        for (int latIdx = 0; latIdx <= latSegments; latIdx++)
        {
            float lat = MathF.PI * 0.5f - (float)latIdx / latSegments * MathF.PI;
            float cosLat = MathF.Cos(lat);
            float sinLat = MathF.Sin(lat);
            for (int lonIdx = 0; lonIdx <= lonSegments; lonIdx++)
            {
                float lon = (float)lonIdx / lonSegments * MathF.PI * 2f;
                float nx = cosLat * MathF.Cos(lon);
                float ny = sinLat;
                float nz = cosLat * MathF.Sin(lon);
                vertices.Add(nx * radius);
                vertices.Add(ny * radius);
                vertices.Add(nz * radius);
                vertices.Add(nx);
                vertices.Add(ny);
                vertices.Add(nz);
                vertices.Add((float)lonIdx / lonSegments);
                vertices.Add((float)latIdx / latSegments);
            }
        }

        int stride = lonSegments + 1;
        for (int latIdx = 0; latIdx < latSegments; latIdx++)
            for (int lonIdx = 0; lonIdx < lonSegments; lonIdx++)
            {
                uint a = (uint)(latIdx * stride + lonIdx);
                uint b = (uint)((latIdx + 1) * stride + lonIdx);
                uint c = (uint)((latIdx + 1) * stride + lonIdx + 1);
                uint d = (uint)(latIdx * stride + lonIdx + 1);
                indices.Add(a); indices.Add(b); indices.Add(d);
                indices.Add(b); indices.Add(c); indices.Add(d);
        }

        _sphereIndexCount = indices.Count;
        _sphereVao = GL.GenVertexArray();
        _sphereVbo = GL.GenBuffer();
        _sphereEbo = GL.GenBuffer();
        GL.BindVertexArray(_sphereVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _sphereVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _sphereEbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.BindVertexArray(0);
    }

    private void CreateSphereGeometry()
    {
        if (_sphereVao != 0)
        {
            GL.DeleteVertexArray(_sphereVao);
            GL.DeleteBuffer(_sphereVbo);
            GL.DeleteBuffer(_sphereEbo);
        }
        CreateSphere(_config.Radius, _config.Segments, _config.Segments);
    }

    private void CreateStars(int count, int seed, float planetRadius)
    {
        var rnd = new Random(seed);
        var stars = new float[count * 8];
        _starCount = count;
        float minR = planetRadius * 3f + 30f;
        float maxR = minR + 50f;
        for (int i = 0; i < count; i++)
        {
            float theta = (float)(rnd.NextDouble() * Math.PI * 2);
            float phi = (float)Math.Acos(2 * rnd.NextDouble() - 1);
            float r = minR + (float)rnd.NextDouble() * (maxR - minR);
            stars[i * 8 + 0] = r * MathF.Sin(phi) * MathF.Cos(theta);
            stars[i * 8 + 1] = r * MathF.Sin(phi) * MathF.Sin(theta);
            stars[i * 8 + 2] = r * MathF.Cos(phi);
            float t = (float)rnd.NextDouble();
            stars[i * 8 + 3] = 0.9f + t * 0.1f;
            stars[i * 8 + 4] = 0.85f + t * 0.15f;
            stars[i * 8 + 5] = 0.7f + t * 0.3f;
            stars[i * 8 + 6] = 0.3f + (float)rnd.NextDouble() * 0.7f;
            stars[i * 8 + 7] = (float)(rnd.NextDouble() * Math.PI * 2);
        }
        if (_starVao != 0) { GL.DeleteVertexArray(_starVao); GL.DeleteBuffer(_starVbo); }
        _starVao = GL.GenVertexArray();
        _starVbo = GL.GenBuffer();
        GL.BindVertexArray(_starVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _starVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, stars.Length * sizeof(float), stars, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 8 * sizeof(float), 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 8 * sizeof(float), 7 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        GL.BindVertexArray(0);
    }

    private int CreateHeightmapTexture(int size)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, size, size, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        return tex;
    }

    private void UpdateHeightmapTexture(int texture, float[,] heightmap)
    {
        int size = heightmap.GetLength(0);
        var data = new byte[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = Math.Clamp(heightmap[x, y], -1f, 1f);
                data[y * size + x] = (byte)((v * 0.5f + 0.5f) * 255f);
            }
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, size, size, OpenTK.Graphics.OpenGL4.PixelFormat.Red, PixelType.UnsignedByte, data);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// Save heightmap to data/: PNG (если GDI+ доступен) и всегда PGM для просмотра (открывается в любом просмотрщике).
    /// </summary>
    private static void SaveHeightmapToDataPng(float[,]? heightmap, string fileName)
    {
        if (heightmap == null) return;
        int w = heightmap.GetLength(0), h = heightmap.GetLength(1);
        string baseDir = AppContext.BaseDirectory ?? ".";
        string dataDir = Path.Combine(baseDir, "data");
        try
        {
            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);
            dataDir = Path.GetFullPath(dataDir);
        }
        catch (Exception ex)
        {
            GameLog.Log($"[Data] Create dir failed: {dataDir} - {ex.Message}");
            try
            {
                dataDir = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "data"));
                if (!Directory.Exists(dataDir))
                    Directory.CreateDirectory(dataDir);
            }
            catch { return; }
        }
        string pathPng = Path.GetFullPath(Path.Combine(dataDir, fileName));
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string pathPgm = Path.GetFullPath(Path.Combine(dataDir, baseName + ".pgm"));
        var raw = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = Math.Clamp(heightmap[x, y], -1f, 1f);
                raw[y * w + x] = (byte)((v * 0.5f + 0.5f) * 255f);
            }
        try
        {
            var header = System.Text.Encoding.ASCII.GetBytes($"P5\n{w} {h}\n255\n");
            using var fs = File.Create(pathPgm);
            fs.Write(header, 0, header.Length);
            fs.Write(raw, 0, raw.Length);
            GameLog.Log($"[Data] Saved PGM {pathPgm}");
        }
        catch (Exception ex)
        {
            GameLog.Log($"[Data] PGM failed {pathPgm}: {ex.Message}");
        }
        try
        {
            float minV = float.MaxValue, maxV = float.MinValue;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    float v = heightmap[x, y];
                    if (v < minV) minV = v;
                    if (v > maxV) maxV = v;
                }
            float range = maxV - minV;
            if (range < 0.0001f) range = 1f;
            using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var rect = new Rectangle(0, 0, w, h);
            var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            int stride = bd.Stride;
            var bytes = new byte[stride * h];
            for (int y = 0; y < h; y++)
            {
                int rowOff = y * stride;
                for (int x = 0; x < w; x++)
                {
                    float norm = (heightmap[x, y] - minV) / range;
                    byte g = (byte)Math.Clamp((int)(norm * 255f), 0, 255);
                    int i = rowOff + x * 3;
                    bytes[i] = bytes[i + 1] = bytes[i + 2] = g;
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bd.Scan0, bytes.Length);
            bmp.UnlockBits(bd);
            bmp.Save(pathPng, ImageFormat.Png);
            GameLog.Log($"[Data] Saved PNG {pathPng}");
        }
        catch (Exception ex)
        {
            GameLog.Log($"[Data] PNG failed {pathPng}: {ex.Message}");
        }
    }

    private int CreateProgram(string vs, string fs)
    {
        int v = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(v, vs);
        GL.CompileShader(v);
        GL.GetShader(v, ShaderParameter.CompileStatus, out int vsOk);
        if (vsOk != (int)All.True) { GameLog.Log($"VS: {GL.GetShaderInfoLog(v)}"); GL.DeleteShader(v); return 0; }

        int f = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(f, fs);
        GL.CompileShader(f);
        GL.GetShader(f, ShaderParameter.CompileStatus, out int fsOk);
        if (fsOk != (int)All.True) { GameLog.Log($"FS: {GL.GetShaderInfoLog(f)}"); GL.DeleteShader(v); GL.DeleteShader(f); return 0; }

        int prog = GL.CreateProgram();
        GL.AttachShader(prog, v);
        GL.AttachShader(prog, f);
        GL.LinkProgram(prog);
        GL.DeleteShader(v);
        GL.DeleteShader(f);
        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int linkOk);
        if (linkOk != (int)All.True) { GameLog.Log($"Link: {GL.GetProgramInfoLog(prog)}"); GL.DeleteProgram(prog); return 0; }
        return prog;
    }

    private void CreateShaders()
    {
        string pvsInline = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;
out vec3 FragPos;
out vec3 Normal;
out float Height;
out vec2 TexCoord;
uniform mat4 model, view, projection;
uniform sampler2D heightmap1;
uniform sampler2D heightmap2;
uniform float blendFactor;
uniform float radius;
uniform float dispScale;
void main(){
  vec2 uv = vec2(aUV.x, 1.0 - aUV.y);
  float h1 = texture(heightmap1, uv).r * 2.0 - 1.0;
  float h2 = texture(heightmap2, uv).r * 2.0 - 1.0;
  float h = mix(h1, h2, clamp(blendFactor, 0.0, 1.0));
  Height = h;
    TexCoord = aUV;
  float disp = h * dispScale;
  vec3 pos = aPos + aNormal * disp;
  FragPos = (model * vec4(pos, 1.0)).xyz;
  Normal = mat3(transpose(inverse(model))) * aNormal;
    gl_Position = projection * view * vec4(FragPos, 1.0);
}";
        string baseDir = AppContext.BaseDirectory ?? ".";
        string vsPath = Path.Combine(baseDir, "Shaders", "planet_vertex.glsl");
        string fsPath = Path.Combine(baseDir, "Shaders", "planet_fragment.glsl");
        string pvs = pvsInline;
        string pfs = "";
        if (File.Exists(vsPath) && File.Exists(fsPath))
        {
            try
            {
                pvs = File.ReadAllText(vsPath);
                pfs = File.ReadAllText(fsPath);
                bool hasUnpack = pvs.Contains("* 2.0 - 1.0") || pvs.Contains("*2.0-1.0");
                if (!hasUnpack)
                {
                    GameLog.Log($"[Planet shader] File missing R8 unpack (\"* 2.0 - 1.0\"), using inline vertex shader. Path: {Path.GetFullPath(vsPath)}");
                    pvs = pvsInline;
                }
                _sphereProgram = CreateProgram(pvs, pfs);
                if (_sphereProgram != 0)
                {
                    _planetShaderFromFile = hasUnpack && File.Exists(vsPath);
                    GameLog.Log($"Planet shaders: {(hasUnpack ? "from files" : "vertex inline (R8 fix), fragment from file")}. VS path: {Path.GetFullPath(vsPath)}");
                    int locH1 = GL.GetUniformLocation(_sphereProgram, "heightmap1");
                    int locH2 = GL.GetUniformLocation(_sphereProgram, "heightmap2");
                    int locDisp = GL.GetUniformLocation(_sphereProgram, "dispScale");
                    GameLog.Log($"[Planet shader] Uniforms: heightmap1={locH1}, heightmap2={locH2}, dispScale={locDisp}");
                    if (locH1 < 0 || locH2 < 0 || locDisp < 0)
                        GameLog.Log("[Planet shader] WARNING: some uniforms missing (displacement may not work)");
                }
            }
            catch (Exception ex)
            {
                GameLog.Log($"Planet shader file load: {ex.Message}");
                pvs = pvsInline;
            }
        }
        if (_sphereProgram == 0)
        {
            _planetShaderFromFile = false;
            pvs = pvsInline;
            pfs = @"#version 330 core
out vec4 FragColor;
in vec3 FragPos, Normal;
in float Height;
in vec2 TexCoord;
uniform vec3 lightPos, lightColor, cameraPos;
uniform float atmosphere, temperature;
float sb(float a, float b, float c) { return smoothstep(b, c, a); }
vec3 coldColor(float h) {
  if (h < -0.05) return mix(vec3(0.1,0.2,0.4), vec3(0.7,0.8,0.9), sb(h,-0.2,-0.05));
  if (h < 0.5) return mix(vec3(0.7,0.8,0.9), vec3(0.9,0.95,1.0), sb(h,-0.05,0.5));
  return mix(vec3(0.9,0.95,1.0), vec3(1.0), sb(h,0.5,0.8));
}
vec3 earthColor(float h) {
  if (h < -0.2) return mix(vec3(0.0,0.1,0.3), vec3(0.0,0.2,0.5), sb(h,-0.35,-0.2));
  if (h < 0.0) return mix(vec3(0.0,0.2,0.5), vec3(0.76,0.7,0.5), sb(h,-0.2,0.0));
  if (h < 0.35) return mix(vec3(0.2,0.5,0.1), vec3(0.1,0.35,0.05), sb(h,0.0,0.35));
  if (h < 0.6) return mix(vec3(0.1,0.35,0.05), vec3(0.4,0.35,0.3), sb(h,0.3,0.6));
  return mix(vec3(0.4,0.35,0.3), vec3(0.95,0.95,1.0), sb(h,0.6,0.95));
}
vec3 hotColor(float h) {
  if (h < -0.05) return mix(vec3(0.3,0.1,0.05), vec3(0.6,0.3,0.1), sb(h,-0.2,-0.05));
  if (h < 0.5) return mix(vec3(0.6,0.3,0.1), vec3(0.5,0.25,0.1), sb(h,-0.05,0.5));
  return mix(vec3(0.5,0.25,0.1), vec3(1.0,0.4,0.1), sb(h,0.5,0.8));
}
vec3 col(float h, float t) {
  float cw = 1.0 - smoothstep(0.0, 0.35, t);
  float hw = smoothstep(0.65, 1.0, t);
  float ew = 1.0 - cw - hw;
  ew = max(ew, 0.0);
  float sum = cw + ew + hw;
  cw /= sum; ew /= sum; hw /= sum;
  return coldColor(h) * cw + earthColor(h) * ew + hotColor(h) * hw;
}
void main(){
  vec3 n = normalize(Normal);
  vec3 L = normalize(lightPos - FragPos);
  float diff = max(dot(n, L), 0.0);
  vec3 base = col(Height, temperature);
  vec3 lit = 0.35 * lightColor * base + diff * lightColor * base;
  vec3 V = normalize(cameraPos - FragPos);
  float rim = 1.0 - max(dot(V, n), 0.0);
  rim = rim * rim;
  vec3 rimC = mix(vec3(0.3,0.5,1.0), vec3(1.0,0.3,0.1), temperature);
  lit += rimC * rim * atmosphere * 1.5;
  lit = max(lit, vec3(0.12, 0.14, 0.18));
  FragColor = vec4(lit, 1.0);
}";
            _sphereProgram = CreateProgram(pvs, pfs);
        }
        if (_sphereProgram == 0)
        {
            GameLog.Log("Planet shader failed, using fallback minimal shader.");
            string pfsFallback = "#version 330 core\nout vec4 FragColor; in vec3 Normal; void main(){ FragColor = vec4(0.25,0.35,0.55,1.0); }\n";
            _sphereProgram = CreateProgram(pvs, pfsFallback);
        }

        string svs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aColor;
layout(location=2) in float aBrightness;
layout(location=3) in float aPhase;
uniform mat4 model, view, projection;
uniform float time;
out vec3 starColor;
out float starBrightness;
void main(){
    gl_Position = projection * view * model * vec4(aPos, 1.0);
  float tw = sin(time*3.0+aPhase)*0.3+0.7;
  tw *= sin(time*7.0+aPhase*2.0)*0.15+0.85;
    starColor = aColor;
  starBrightness = aBrightness * tw;
    gl_PointSize = 1.5 + aBrightness * 2.0;
}";
        string sfs = @"#version 330 core
out vec4 FragColor;
in vec3 starColor;
in float starBrightness;
void main(){
  vec2 c = gl_PointCoord - vec2(0.5);
  if (length(c) > 0.5) discard;
  float e = 1.0 - smoothstep(0.3, 0.5, length(c));
  FragColor = vec4(starColor * starBrightness, e);
}";
        _starProgram = CreateProgram(svs, sfs);
        
        string avs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aColor;
layout(location=2) in float aSize;
uniform mat4 model, view, projection;
out vec3 agentColor;
void main(){
    gl_Position = projection * view * model * vec4(aPos, 1.0);
    agentColor = aColor;
    gl_PointSize = aSize;
}";
        string afs = @"#version 330 core
out vec4 FragColor;
in vec3 agentColor;
void main(){
  vec2 c = gl_PointCoord - vec2(0.5);
  if (length(c) > 0.5) discard;
  float e = 1.0 - smoothstep(0.3, 0.5, length(c));
  FragColor = vec4(agentColor, e);
}";
        _pawnProgram = CreateProgram(avs, afs);
        _pawnVao = GL.GenVertexArray();
        _pawnVbo = GL.GenBuffer();
        
        string p3dvs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
uniform mat4 model, view, projection;
out vec3 FragPos;
out vec3 Normal;
void main(){
  vec4 w = model * vec4(aPos, 1.0);
  FragPos = w.xyz;
  Normal = mat3(transpose(inverse(model))) * aNormal;
  gl_Position = projection * view * w;
}";
        string p3dfs = @"#version 330 core
out vec4 FragColor;
in vec3 FragPos, Normal;
uniform vec3 lightPos, lightColor;
void main(){
  vec3 n = normalize(Normal);
  vec3 L = normalize(lightPos - FragPos);
  float diff = max(dot(n, L), 0.0);
  vec3 base = vec3(0.95, 0.85, 0.25);
  vec3 lit = 0.35 * lightColor * base + diff * lightColor * base;
  lit = max(lit, vec3(0.15, 0.15, 0.18));
  FragColor = vec4(lit, 1.0);
}";
        _pawn3DProgram = CreateProgram(p3dvs, p3dfs);
        CreatePawn3DGeometry();
    }

    private void CreatePawn3DGeometry()
    {
        var vertices = new List<float>();
        var indices = new List<uint>();
        const int segs = 8;
        const float bodyRad = 0.12f;
        const float bodyY0 = -0.35f;
        const float bodyY1 = 0.35f;
        const float headY = 0.5f;
        const float headRad = 0.1f;
        uint idx = 0;
        for (int i = 0; i <= segs; i++)
        {
            float t = (float)i / segs * (float)Math.PI * 2f;
            float cx = MathF.Cos(t);
            float sz = MathF.Sin(t);
            float nx = cx;
            float nz = sz;
            vertices.Add(bodyRad * cx); vertices.Add(bodyY0); vertices.Add(bodyRad * sz);
            vertices.Add(nx); vertices.Add(0f); vertices.Add(nz);
            indices.Add(idx++);
            vertices.Add(bodyRad * cx); vertices.Add(bodyY1); vertices.Add(bodyRad * sz);
            vertices.Add(nx); vertices.Add(0f); vertices.Add(nz);
            indices.Add(idx++);
        }
        for (int i = 0; i < segs; i++)
        {
            uint a = (uint)(i * 2);
            uint b = a + 1;
            uint c = (uint)((i + 1) % (segs + 1) * 2);
            uint d = c + 1;
            indices.Add(a); indices.Add(b); indices.Add(c);
            indices.Add(b); indices.Add(d); indices.Add(c);
        }
        int bodyVerts = (segs + 1) * 2;
        int headSegs = 8;
        int headRings = 2;
        for (int ring = 0; ring <= headRings; ring++)
        {
            float phi = (float)ring / headRings * (float)Math.PI * 0.5f;
            int n = (ring == 0 || ring == headRings) ? 1 : headSegs;
            for (int i = 0; i < n; i++)
            {
                float th = n == 1 ? 0f : (float)i / n * (float)Math.PI * 2f;
                float sx = MathF.Sin(phi) * MathF.Cos(th);
                float sz = MathF.Sin(phi) * MathF.Sin(th);
                float x = headRad * sx;
                float y = headY + headRad * MathF.Cos(phi);
                float z = headRad * sz;
                vertices.Add(x); vertices.Add(y); vertices.Add(z);
                vertices.Add(sx); vertices.Add(MathF.Cos(phi)); vertices.Add(sz);
                indices.Add(idx++);
            }
        }
        int headStart = bodyVerts;
        for (int ring = 0; ring < headRings; ring++)
        {
            int n0 = (ring == 0) ? 1 : headSegs;
            int n1 = (ring + 1 == headRings) ? 1 : headSegs;
            int off0 = ring == 0 ? 0 : (1 + (ring - 1) * headSegs);
            int off1 = 1 + ring * headSegs;
            for (int i = 0; i < n0; i++)
                for (int j = 0; j < n1; j++)
                {
                    uint a = (uint)(headStart + off0 + (i % n0));
                    uint b = (uint)(headStart + off1 + (j % n1));
                    uint c = (uint)(headStart + off1 + ((j + 1) % n1));
                    indices.Add(a); indices.Add(b); indices.Add(c);
                    if (n0 > 1 && n1 > 1)
                    {
                        uint d = (uint)(headStart + off0 + ((i + 1) % n0));
                        indices.Add(a); indices.Add(c); indices.Add(d);
                    }
                }
        }
        _pawn3DIndexCount = indices.Count;
        _pawn3DVao = GL.GenVertexArray();
        _pawn3DVbo = GL.GenBuffer();
        _pawn3DEbo = GL.GenBuffer();
        GL.BindVertexArray(_pawn3DVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _pawn3DVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _pawn3DEbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.BindVertexArray(0);
    }

    private void InitializeAgents(bool forceReset = false)
    {
        if (_pawnsInitialized && !forceReset) return;

        if (_pawnAgent == null || forceReset)
        {
            _pawnAgent = new PawnAgent();
        }

        if (_currentHeightmap != null)
        {
            _pawnAgent.SetSurfaceState(_currentHeightmap, _nextHeightmap, _blendFactor, EffectiveDisplacementScale());
        }
        float effT = WorldConstants.GetEffectiveTemperature(_config.Temperature, _config.GeologicActivity);
        float effA = WorldConstants.GetEffectiveAtmosphere(_config.Atmosphere, _config.GeologicActivity);
        _pawnAgent.SetPlanetParameters(_config.Radius, _config.Density, effT, effA, _config.GeologicActivity);

        // Species baseline: если пользователь задал ДНК — используем её как мастер, иначе строим blueprint от планеты.
        var rnd = new Random(_config.Seed);
        DnaSequence? userSeq = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(_speciesDnaText))
            {
                var seq = new DnaSequence(_speciesDnaText);
                if (seq.IsViable) userSeq = seq;
            }
        }
        catch
        {
            userSeq = null;
        }

        _speciesBlueprint = SpeciesBlueprint.ForPlanet(_config, rnd, userSeq);
        _speciesDnaText = _speciesBlueprint.MasterSequence.Raw;
        _pawnAgent.SetSpeciesDna(_speciesBlueprint.MasterSequence);

        _pawnAgent.InitializePopulation(_pawnInitialCount);
        _pawnsInitialized = true;
        int alive = _pawnAgent.AliveCount;
        GameLog.Log($"[Pawns] Initialized: total={_pawnInitialCount}, alive={alive}, showPawns={_showPawns}");
        if (alive == 0) GameLog.Log("[Pawns] WARNING: no alive pawns after init (they may die immediately or spawn failed)");
    }

    private void UpdateAgents(float dt)
    {
        if (_pawnAgent == null || !_pawnsInitialized) return;
        if (_currentHeightmap != null)
        {
            _pawnAgent.SetSurfaceState(_currentHeightmap, _nextHeightmap, _blendFactor, EffectiveDisplacementScale());
        }
        float effT = WorldConstants.GetEffectiveTemperature(_config.Temperature, _config.GeologicActivity);
        float effA = WorldConstants.GetEffectiveAtmosphere(_config.Atmosphere, _config.GeologicActivity);
        _pawnAgent.SetPlanetParameters(_config.Radius, _config.Density, effT, effA, _config.GeologicActivity);
        _pawnAgent.Update(dt);

        _pawnSurfaceAuditTimer += dt;
        if (_pawnSurfaceAuditTimer >= 2f)
        {
            _pawnSurfaceAuditTimer = 0f;
            int invalid = _pawnAgent.ValidateSurfaceAnchoring();
            var dbg = _pawnAgent.GetSurfaceDebugInfo();
            GameLog.Log($"[PawnsSurface] invalid={invalid}/{_pawnAgent.AliveCount} below={dbg.BelowSurfaceCount}/{dbg.AliveCount} " +
                        $"worldR={dbg.AverageWorldRadius:F3} surfaceR={dbg.AverageSurfaceRadius:F3} delta={dbg.AverageVisualDelta:F4} " +
                        $"h={dbg.AverageHeight:F4} disp={dbg.DisplacementScale:F4} blend={dbg.BlendFactor:F3}");
        }
    }

    private void DrawAgents(Matrix4 model, Matrix4 view, Matrix4 projection)
    {
        if (_pawnAgent == null || !_showPawns || _pawnAgent.AliveCount == 0)
        {
            if (!_pawnSkipReasonLogged)
            {
                _pawnSkipReasonLogged = true;
                string reason = _pawnAgent == null ? "agent=null" : !_showPawns ? "showPawns=false" : "AliveCount=0";
                GameLog.Log($"[Pawns] Draw skipped: {reason} (total={_pawnAgent?.TotalCount ?? 0}, alive={_pawnAgent?.AliveCount ?? 0})");
            }
            return;
        }
        var pawns = _pawnAgent.Pawns;
        int aliveCount = 0;
        foreach (var p in pawns) if (p.IsAlive) aliveCount++;
        if (aliveCount == 0)
        {
            if (!_pawnSkipReasonLogged)
            {
                _pawnSkipReasonLogged = true;
                GameLog.Log("[Pawns] Draw skipped: aliveCount=0 after recount");
            }
            return;
        }

        bool use3D = _use3DPawns && _pawn3DVao != 0 && _pawn3DProgram != 0;
        bool usePoints = _pawnProgram != 0 && _pawnVao != 0;
        if (!_pawnDrawModeLogged)
        {
            _pawnDrawModeLogged = true;
            if (use3D)
                GameLog.Log($"[Pawns] Drawing: mode=3D, count={aliveCount}");
            else if (usePoints)
                GameLog.Log($"[Pawns] Drawing: mode=points, count={aliveCount}");
            else
                GameLog.Log($"[Pawns] WARNING: neither 3D nor points available (3D: vao={_pawn3DVao}, prog={_pawn3DProgram}; points: vao={_pawnVao}, prog={_pawnProgram})");
        }

        if (use3D)
        {
            Vector3 lightPos = new Vector3(2f, 2f, 1f);
            Vector3 lightColor = new Vector3(1f, 1f, 0.95f);
            GL.UseProgram(_pawn3DProgram);
            GL.UniformMatrix4(GL.GetUniformLocation(_pawn3DProgram, "view"), false, ref view);
            GL.UniformMatrix4(GL.GetUniformLocation(_pawn3DProgram, "projection"), false, ref projection);
            GL.Uniform3(GL.GetUniformLocation(_pawn3DProgram, "lightPos"), lightPos);
            GL.Uniform3(GL.GetUniformLocation(_pawn3DProgram, "lightColor"), lightColor);
            GL.BindVertexArray(_pawn3DVao);
            foreach (var p in pawns)
            {
                if (!p.IsAlive) continue;
                var w = _pawnAgent.GetWorldPosition(p);
                Vector3 up = p.Position.Normalized();
                Vector3 forward = Vector3.Cross(up, Vector3.UnitY);
                if (forward.LengthSquared < 0.01f) forward = Vector3.Cross(up, Vector3.UnitX);
                forward = forward.Normalized();
                Vector3 right = Vector3.Cross(forward, up).Normalized();
                var rot = new Matrix4(
                    right.X, right.Y, right.Z, 0f,
                    up.X, up.Y, up.Z, 0f,
                    -forward.X, -forward.Y, -forward.Z, 0f,
                    0f, 0f, 0f, 1f);
                float tilt = p.Genome.LegCount > 2 ? 0.4f : 0f;
                var tiltRot = Matrix4.CreateRotationX(tilt);
                rot = rot * tiltRot;
                float scale = 0.4f * p.Genome.Size;
                var scaleM = Matrix4.CreateScale(scale);
                var trans = Matrix4.CreateTranslation(w.X, w.Y, w.Z);
                var pawnModel = trans * rot * scaleM;
                pawnModel = model * pawnModel;
                GL.UniformMatrix4(GL.GetUniformLocation(_pawn3DProgram, "model"), false, ref pawnModel);
                GL.DrawElements(PrimitiveType.Triangles, _pawn3DIndexCount, DrawElementsType.UnsignedInt, 0);
            }
            GL.BindVertexArray(0);
        }
        else if (usePoints)
        {
            var verts = new float[aliveCount * 7];
            int idx = 0;
            foreach (var p in pawns)
            {
                if (!p.IsAlive) continue;
                var w = _pawnAgent.GetWorldPosition(p);
                verts[idx * 7 + 0] = w.X;
                verts[idx * 7 + 1] = w.Y;
                verts[idx * 7 + 2] = w.Z;
                float er = p.EnergyPercent;
                verts[idx * 7 + 3] = 1f - er * 0.5f;
                verts[idx * 7 + 4] = er;
                verts[idx * 7 + 5] = 0.2f;
                verts[idx * 7 + 6] = 28f + p.Genome.Size * 12f;
                idx++;
            }
            GL.BindVertexArray(_pawnVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _pawnVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 7 * sizeof(float), 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.UseProgram(_pawnProgram);
            GL.UniformMatrix4(GL.GetUniformLocation(_pawnProgram, "model"), false, ref model);
            GL.UniformMatrix4(GL.GetUniformLocation(_pawnProgram, "view"), false, ref view);
            GL.UniformMatrix4(GL.GetUniformLocation(_pawnProgram, "projection"), false, ref projection);
            GL.DrawArrays(PrimitiveType.Points, 0, aliveCount);
            GL.BindVertexArray(0);
        }
    }

    private static void DrawCapsule(ImDrawListPtr dl, System.Numerics.Vector2 from, System.Numerics.Vector2 to, float radius, uint fillCol, uint outlineCol)
    {
        float thick = radius * 2f;
        dl.AddLine(from, to, fillCol, thick);
        dl.AddCircleFilled(from, radius, fillCol);
        dl.AddCircleFilled(to, radius, fillCol);
        dl.AddCircle(from, radius, outlineCol, 0, 1.2f);
        dl.AddCircle(to, radius, outlineCol, 0, 1.2f);
    }

    private static void DrawCreatureSchematic(ImDrawListPtr drawList,
        System.Numerics.Vector2 topLeft, float width, float height,
        PawnGenome genome, List<char> activeSegments, BaseTrait traits)
    {
        float cx = topLeft.X + width * 0.5f;
        float cy = topLeft.Y + height * 0.5f;

        uint colBody = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.4f, 0.65f, 0.4f, 1f));
        uint colOutline = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.2f, 0.35f, 0.2f, 1f));
        uint colLimb = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.35f, 0.5f, 0.35f, 1f));
        uint colLimbOutline = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.2f, 0.32f, 0.2f, 1f));
        uint colHead = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.5f, 0.7f, 0.5f, 1f));
        uint colEye = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.95f, 0.95f, 0.3f, 1f));
        uint colWing = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.6f, 0.5f, 0.7f, 0.85f));
        uint colFin = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.3f, 0.5f, 0.7f, 0.9f));

        if ((traits & (BaseTrait.SwimmerSurface | BaseTrait.SwimmerDeep | BaseTrait.FinLikeLimbs)) != 0)
        {
            colBody = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.35f, 0.55f, 0.65f, 1f));
            colLimb = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.3f, 0.5f, 0.6f, 1f));
            colLimbOutline = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.18f, 0.35f, 0.45f, 1f));
        }
        if ((traits & (BaseTrait.TrueFlight | BaseTrait.Glider | BaseTrait.WingLikeLimbs)) != 0)
        {
            colBody = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.55f, 0.5f, 0.6f, 1f));
        }

        float bodyRad = 14f + genome.Size * 8f;
        bodyRad = Math.Clamp(bodyRad, 12f, 28f);

        int legCount = Math.Clamp(genome.LegCount, 1, 8);
        int armCount = activeSegments.Count(c => c == '1');
        armCount = Math.Clamp(armCount, 0, 4);
        int eyeCount = Math.Clamp(activeSegments.Count(c => c == '5'), 0, 6);
        if (eyeCount == 0 && genome.CanSeeFood) eyeCount = 2;
        bool hasHead = activeSegments.Contains('4') || genome.HasMind;

        float legLen = 18f + legCount * 2f;
        float legCapsuleRad = 4.5f;
        float stepAngle = MathF.PI / (legCount + 1);
        for (int i = 0; i < legCount; i++)
        {
            float a = MathF.PI * 0.5f + (i - legCount * 0.5f) * stepAngle * 0.7f;
            var from = new System.Numerics.Vector2(cx + MathF.Cos(a) * bodyRad * 0.85f, cy + MathF.Sin(a) * bodyRad * 0.85f);
            var to = new System.Numerics.Vector2(from.X + MathF.Cos(a) * legLen, from.Y + MathF.Sin(a) * legLen);
            DrawCapsule(drawList, from, to, legCapsuleRad, colLimb, colLimbOutline);
        }

        float armLen = 14f;
        float armCapsuleRad = 3.5f;
        for (int i = 0; i < armCount; i++)
        {
            float a = MathF.PI * 0.5f + (i - armCount * 0.5f) * 0.4f - (armCount == 1 ? 0f : MathF.PI * 0.5f);
            var from = new System.Numerics.Vector2(cx + MathF.Cos(a) * bodyRad, cy - MathF.Sin(a) * bodyRad * 0.5f);
            var to = new System.Numerics.Vector2(from.X + MathF.Cos(a) * armLen, from.Y - MathF.Sin(a) * armLen);
            DrawCapsule(drawList, from, to, armCapsuleRad, colLimb, colLimbOutline);
        }

        drawList.AddCircleFilled(new System.Numerics.Vector2(cx, cy), bodyRad, colBody);
        drawList.AddCircle(new System.Numerics.Vector2(cx, cy), bodyRad, colOutline, 0, 1.8f);

        if (hasHead)
        {
            float headY = cy - bodyRad - 6f;
            float headRad = 9f;
            drawList.AddCircleFilled(new System.Numerics.Vector2(cx, headY), headRad, colHead);
            drawList.AddCircle(new System.Numerics.Vector2(cx, headY), headRad, colOutline, 0, 1.5f);

            float eyeRad = 2.8f;
            for (int e = 0; e < eyeCount; e++)
            {
                float ex = cx + (e - eyeCount * 0.5f) * 4.2f;
                float ey = headY - 2f;
                drawList.AddCircleFilled(new System.Numerics.Vector2(ex, ey), eyeRad, colEye);
                drawList.AddCircle(new System.Numerics.Vector2(ex, ey), eyeRad, colOutline, 0, 1f);
            }
        }

        if ((traits & (BaseTrait.TrueFlight | BaseTrait.Glider | BaseTrait.WingLikeLimbs)) != 0)
        {
            var w1 = new System.Numerics.Vector2(cx - 35f, cy - 15f);
            var w2 = new System.Numerics.Vector2(cx - 20f, cy - 25f);
            var w3 = new System.Numerics.Vector2(cx, cy - 20f);
            drawList.AddTriangleFilled(w1, w2, w3, colWing);
            w1 = new System.Numerics.Vector2(cx + 35f, cy - 15f);
            w2 = new System.Numerics.Vector2(cx + 20f, cy - 25f);
            w3 = new System.Numerics.Vector2(cx, cy - 20f);
            drawList.AddTriangleFilled(w1, w2, w3, colWing);
        }

        if ((traits & (BaseTrait.SwimmerSurface | BaseTrait.SwimmerDeep | BaseTrait.FinLikeLimbs)) != 0)
        {
            var f1 = new System.Numerics.Vector2(cx - 12f, cy + bodyRad + 5f);
            var f2 = new System.Numerics.Vector2(cx, cy + bodyRad + 22f);
            var f3 = new System.Numerics.Vector2(cx + 12f, cy + bodyRad + 5f);
            drawList.AddTriangleFilled(f1, f2, f3, colFin);
        }
    }

    private void SaveConfig()
    {
        try { _config.Volume = _masterVolume; _config.Save(_configPath); }
        catch (Exception ex) { GameLog.Log($"Save config: {ex.Message}"); }
    }

    private void ApplyPlanetMutation()
    {
        var rnd = new Random();
        int fields = Math.Clamp(_config.MutationFields, 1, 7);
        var chosen = new HashSet<int>();
        while (chosen.Count < fields)
            chosen.Add(rnd.Next(1, 8)); // 1..7

        foreach (int f in chosen)
        {
            switch (f)
            {
                case 1: // Seed
                    _config.Seed = Math.Max(0, _config.Seed + rnd.Next(-50000, 50001));
                    break;
                case 2: // Geologic
                    _config.GeologicActivity = Math.Clamp(_config.GeologicActivity + ((float)rnd.NextDouble() - 0.5f) * 0.35f, 0f, 2f);
                    break;
                case 3: // Octaves
                    _config.NoiseOctaves = Math.Clamp(_config.NoiseOctaves + (rnd.NextDouble() < 0.5 ? -1 : 1), 1, 8);
                    break;
                case 4: // Noise frequency
                    _config.NoiseFrequency = Math.Clamp(_config.NoiseFrequency + ((float)rnd.NextDouble() - 0.5f) * 0.25f, 0.01f, 2f);
                    break;
                case 5: // Temperature
                    _config.Temperature = Math.Clamp(_config.Temperature + ((float)rnd.NextDouble() - 0.5f) * 0.18f, 0f, 1f);
                    break;
                case 6: // Atmosphere
                    _config.Atmosphere = Math.Clamp(_config.Atmosphere + ((float)rnd.NextDouble() - 0.5f) * 0.18f, 0f, 1f);
                    break;
                case 7: // Density
                    _config.Density = Math.Clamp(_config.Density + ((float)rnd.NextDouble() - 0.5f) * 0.4f, 0.1f, 5f);
                    break;
            }
        }

        SaveConfig();
        UpdateTitle();
        RequestPlanetRegenerate(immediate: false);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        float dt = (float)args.Time;

        // Camera control
        var mouse = MouseState;
        if (mouse.ScrollDelta.Y != 0)
        {
            _cameraDistance -= mouse.ScrollDelta.Y * _cameraDistance * 0.05f;
            _cameraDistance = Math.Clamp(_cameraDistance, 20f, 120f);
        }
        if (mouse[0])
        {
            var cur = mouse.Position;
            if (_lastMousePos != Vector2.Zero)
            {
                _rotationY += (cur.X - _lastMousePos.X) * 0.01f;
                _rotationX += (cur.Y - _lastMousePos.Y) * 0.01f;
                _rotationX = Math.Clamp(_rotationX, -MathF.PI / 2f, MathF.PI / 2f);
            }
            _lastMousePos = cur;
        }
        else
        {
            _lastMousePos = Vector2.Zero;
        }

        // Hotkeys
        var keyboard = KeyboardState;
        if (keyboard.IsKeyDown(Keys.Escape) && !_prevEsc) _showMenu = !_showMenu;
        _prevEsc = keyboard.IsKeyDown(Keys.Escape);
        if (keyboard.IsKeyDown(Keys.F1) && !_prevKeyboard.IsKeyDown(Keys.F1)) _showSliders = !_showSliders;
        if ((keyboard.IsKeyDown(Keys.F2) || keyboard.IsKeyDown(Keys.GraveAccent)) &&
            !_prevKeyboard.IsKeyDown(Keys.F2) && !_prevKeyboard.IsKeyDown(Keys.GraveAccent))
            _showConsole = !_showConsole;

        // Step-by-step planet edits (keyboard)
        float stepT = 0.05f;
        float stepA = 0.05f;
        float stepG = 0.10f;
        float stepF = 0.05f;
        if (keyboard.IsKeyDown(Keys.D1) && !_prevKeyboard.IsKeyDown(Keys.D1)) { _config.Temperature = Math.Clamp(_config.Temperature + stepT, 0f, 1f); UpdateTitle(); SaveConfig(); }
        if (keyboard.IsKeyDown(Keys.D2) && !_prevKeyboard.IsKeyDown(Keys.D2)) { _config.Temperature = Math.Clamp(_config.Temperature - stepT, 0f, 1f); UpdateTitle(); SaveConfig(); }
        if (keyboard.IsKeyDown(Keys.D3) && !_prevKeyboard.IsKeyDown(Keys.D3)) { _config.Atmosphere = Math.Clamp(_config.Atmosphere + stepA, 0f, 1f); UpdateTitle(); SaveConfig(); }
        if (keyboard.IsKeyDown(Keys.D4) && !_prevKeyboard.IsKeyDown(Keys.D4)) { _config.Atmosphere = Math.Clamp(_config.Atmosphere - stepA, 0f, 1f); UpdateTitle(); SaveConfig(); }
        if (keyboard.IsKeyDown(Keys.D5) && !_prevKeyboard.IsKeyDown(Keys.D5)) { _config.GeologicActivity = Math.Clamp(_config.GeologicActivity + stepG, 0f, 2f); UpdateTitle(); SaveConfig(); RequestPlanetRegenerate(false); }
        if (keyboard.IsKeyDown(Keys.D6) && !_prevKeyboard.IsKeyDown(Keys.D6)) { _config.GeologicActivity = Math.Clamp(_config.GeologicActivity - stepG, 0f, 2f); UpdateTitle(); SaveConfig(); RequestPlanetRegenerate(false); }
        if (keyboard.IsKeyDown(Keys.D7) && !_prevKeyboard.IsKeyDown(Keys.D7)) { _config.NoiseFrequency = Math.Clamp(_config.NoiseFrequency + stepF, 0.01f, 2f); UpdateTitle(); SaveConfig(); RequestPlanetRegenerate(false); }
        if (keyboard.IsKeyDown(Keys.D8) && !_prevKeyboard.IsKeyDown(Keys.D8)) { _config.NoiseFrequency = Math.Clamp(_config.NoiseFrequency - stepF, 0.01f, 2f); UpdateTitle(); SaveConfig(); RequestPlanetRegenerate(false); }

        _prevKeyboard = keyboard;

        // Smooth visual params (target = effective: geology adds heat and atmosphere)
        float effTemp = WorldConstants.GetEffectiveTemperature(_config.Temperature, _config.GeologicActivity);
        float effAtm = WorldConstants.GetEffectiveAtmosphere(_config.Atmosphere, _config.GeologicActivity);
        _temperatureVis = SmoothStepTo(_temperatureVis, effTemp, dt, 3.5f);
        _atmosphereVis = SmoothStepTo(_atmosphereVis, effAtm, dt, 3.5f);

        // Async generation + smooth heightmap blend
        PumpPendingGeneration(dt);

        // Auto-mutation
        if (_config.AutoMutationInterval > 0.01f)
        {
            _autoMutationTimer += dt;
            if (_autoMutationTimer >= _config.AutoMutationInterval)
            {
                _autoMutationTimer = 0f;
                ApplyPlanetMutation();
            }
        }
        else
        {
            _autoMutationTimer = 0f;
        }

        // Audio
        if (_audio != null)
        {
            _audio.Volume = _masterVolume;
            _audio.IsEnabled = _audioEnabled;
            _audio.UpdateParameters(_temperatureVis, _atmosphereVis, _config.GeologicActivity, _config.Seed);
            _audio.Update();
        }

        // Render
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.DepthMask(true);
        GL.DepthFunc(DepthFunction.Less);

        _angle += 8f * dt;
        _shaderTime += dt;
        var view = Matrix4.CreateTranslation(0, 0, -_cameraDistance);
        float aspect = ClientSize.X / (float)ClientSize.Y;
        float far = Math.Max(500f, _cameraDistance + _config.Radius * 5f + 100f);
        var projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), aspect, 0.1f, far);
        var model = Matrix4.CreateRotationX(_rotationX) * Matrix4.CreateRotationY(_rotationY + MathHelper.DegreesToRadians(_angle));

        if (_sphereVao == 0) CreateSphereGeometry();
        if (_sphereVao != 0 && _sphereIndexCount > 0 && _sphereProgram != 0)
        {
        GL.UseProgram(_sphereProgram);
        GL.UniformMatrix4(GL.GetUniformLocation(_sphereProgram, "model"), false, ref model);
        GL.UniformMatrix4(GL.GetUniformLocation(_sphereProgram, "view"), false, ref view);
        GL.UniformMatrix4(GL.GetUniformLocation(_sphereProgram, "projection"), false, ref projection);
            GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "blendFactor"), _isTransitioning ? _blendFactor : 0f);
            GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "dispScale"), EffectiveDisplacementScale());
            GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "radius"), _config.Radius);
            GL.Uniform3(GL.GetUniformLocation(_sphereProgram, "lightPos"), 2f, 2f, 1f);
            GL.Uniform3(GL.GetUniformLocation(_sphereProgram, "lightColor"), 1f, 1f, 0.95f);
            GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "atmosphere"), _atmosphereVis);
            GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "temperature"), _temperatureVis);
            GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "seed"), (float)_config.Seed);
            GL.Uniform3(GL.GetUniformLocation(_sphereProgram, "cameraPos"), 0f, 0f, _cameraDistance);
            GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "geologicActivity"), _config.GeologicActivity);
            GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "time"), _shaderTime);
        GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "cloudDensity"), 0.5f);
            int seedIntLoc = GL.GetUniformLocation(_sphereProgram, "seed");
            if (seedIntLoc >= 0) GL.Uniform1(seedIntLoc, _config.Seed);

            int tex0 = CurrentHeightmapTexture;
            int tex1 = _isTransitioning ? NextHeightmapTexture : tex0;
        GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, tex0);
        GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "heightmap1"), 0);
        GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, tex1);
        GL.Uniform1(GL.GetUniformLocation(_sphereProgram, "heightmap2"), 1);
        
            if (!_planetDiagnosticsLogged && _currentHeightmap != null)
            {
                _planetDiagnosticsLogged = true;
                float dispScale = EffectiveDisplacementScale();
                int w = _currentHeightmap.GetLength(0), h = _currentHeightmap.GetLength(1);
                float minH = float.MaxValue, maxH = float.MinValue;
                for (int i = 0; i < w; i++)
                    for (int j = 0; j < h; j++)
                    {
                        float v = _currentHeightmap[i, j];
                        if (v < minH) minH = v;
                        if (v > maxH) maxH = v;
                    }
                GameLog.Log($"[Planet draw] dispScale={dispScale:F4} (radius-scaled), Radius={_config.Radius}");
                GameLog.Log($"[Planet draw] Heightmap {w}x{h}: min={minH:F4}, max={maxH:F4}, fromFile={_planetShaderFromFile}, tex0={CurrentHeightmapTexture}, tex1={(_isTransitioning ? NextHeightmapTexture : CurrentHeightmapTexture)}");

                // CPU terrain samples for sanity (same logic as pawns).
                try
                {
                    if (_pawnAgent != null)
                    {
                        float hx = _pawnAgent.GetTerrainHeightAt(Vector3.UnitX);
                        float hy = _pawnAgent.GetTerrainHeightAt(Vector3.UnitY);
                        float hz = _pawnAgent.GetTerrainHeightAt(Vector3.UnitZ);
                        GameLog.Log($"[Planet draw] CPU heights (world units): X={hx:F3}, Y={hy:F3}, Z={hz:F3}");
                    }
                }
                catch (Exception ex)
                {
                    GameLog.Log($"[Planet draw] CPU height sample failed: {ex.Message}");
                }

                // CPU min/max after scaling, чтобы оценить амплитуду рельефа.
                try
                {
                    if (_currentHeightmap != null)
                    {
                        float mn = float.MaxValue, mx = float.MinValue;
                        float absNearZero = 0f;
                        int total = w * h;
                        for (int i = 0; i < w; i++)
                            for (int j = 0; j < h; j++)
                            {
                                float v = _currentHeightmap[i, j] * EffectiveDisplacementScale();
                                if (v < mn) mn = v;
                                if (v > mx) mx = v;
                                if (MathF.Abs(v) < 0.02f) absNearZero += 1f;
                            }
                        GameLog.Log($"[Planet draw] CPU scaled height range: min={mn:F3}, max={mx:F3}, nearZero(<0.02)={(absNearZero / total):P1}");
                    }
                }
                catch (Exception ex)
                {
                    GameLog.Log($"[Planet draw] CPU min/max scaled failed: {ex.Message}");
                }
            }

        GL.BindVertexArray(_sphereVao);
        GL.DrawElements(PrimitiveType.Triangles, _sphereIndexCount, DrawElementsType.UnsignedInt, 0);
        }

        GL.UseProgram(_starProgram);
        var starModel = Matrix4.CreateRotationX(_rotationX) * Matrix4.CreateRotationY(_rotationY + MathHelper.DegreesToRadians(_angle));
        GL.UniformMatrix4(GL.GetUniformLocation(_starProgram, "model"), false, ref starModel);
        GL.UniformMatrix4(GL.GetUniformLocation(_starProgram, "view"), false, ref view);
        GL.UniformMatrix4(GL.GetUniformLocation(_starProgram, "projection"), false, ref projection);
        GL.Uniform1(GL.GetUniformLocation(_starProgram, "time"), dt);
        GL.BindVertexArray(_starVao);
        GL.DrawArrays(PrimitiveType.Points, 0, _starCount);

        UpdateAgents(dt);
        DrawAgents(model, view, projection);
        
        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);
        
        // UI
        if (_imGuiController != null)
        {
            _imGuiController.Update(this, args);
            
            if (_showSliders)
            {
                float sw = ClientSize.X, sh = ClientSize.Y;
                float pw = Math.Min(340f, sw * 0.24f);
                float px = sw - sw * 0.02f - pw;
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(px, sh * 0.05f));
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(pw, sh * 0.9f));
                ImGui.Begin(_loc.T("Planet"), ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

                if (ImGui.BeginTabBar("##layers"))
                {
                    if (ImGui.BeginTabItem(_loc.T("Creature")))
                    {
                        bool sp = _showPawns;
                        if (ImGui.Checkbox(_loc.T("ShowPawns"), ref sp)) _showPawns = sp;
                        bool use3d = _use3DPawns;
                        if (ImGui.Checkbox(_loc.T("Pawns3D"), ref use3d)) _use3DPawns = use3d;
                        int pc = _pawnInitialCount;
                        if (ImGui.SliderInt(_loc.T("Pawns"), ref pc, 1, 200)) { _pawnInitialCount = pc; }

                        ImGui.Separator();
                        ImGui.TextUnformatted(_loc.T("SpeciesDNA"));
                        ImGui.InputText("##speciesdna", ref _speciesDnaText, 1024);

                        if (ImGui.Button(_loc.T("RandomizeDNA"))) _speciesDnaText = DnaSequence.Random(10).Raw;
                ImGui.SameLine();
                        if (ImGui.Button(_loc.T("MutateDNA")))
                        {
                            try { _speciesDnaText = new DnaSequence(_speciesDnaText).Mutate(0.35f).Raw; } catch { }
                        }

                        if (ImGui.Button(_loc.T("ApplyDNA"))) InitializeAgents(forceReset: true);

                        // Preview: schematic appearance from DNA + traits
                        ImGui.Spacing();
                        ImGui.TextUnformatted(_loc.T("CreatureAppearance"));
                        var previewMinC = ImGui.GetCursorScreenPos();
                        float previewW = 220f;
                        float previewH = 200f;
                        ImGui.Dummy(new System.Numerics.Vector2(previewW, previewH));
                        var drawListC = ImGui.GetWindowDrawList();
                        PawnGenome? previewGenome = null;
                        if (_pawnAgent != null)
                        {
                            foreach (var p in _pawnAgent.Pawns)
                            {
                                if (p.IsAlive) { previewGenome = p.Genome; break; }
                            }
                        }
                        if (previewGenome == null && !string.IsNullOrWhiteSpace(_speciesDnaText))
                        {
                            try
                            {
                                var seq = new DnaSequence(_speciesDnaText);
                                if (seq.IsViable) previewGenome = seq.Express(0);
                            }
                            catch { }
                        }
                        if (previewGenome == null) previewGenome = new PawnGenome();
                        var activeSegmentsC = DnaInterpreter.ParseActiveSegments(_speciesDnaText);
                        BaseTrait traitsC = _speciesBlueprint?.Traits ?? BaseTrait.None;
                        DrawCreatureSchematic(drawListC, previewMinC, previewW, previewH, previewGenome, activeSegmentsC, traitsC);

                        if (_speciesBlueprint != null)
                        {
                ImGui.Separator();
                            ImGui.Text(_loc.T("TraitsHeader"));
                            foreach (var line in _speciesBlueprint.GetSummaryLines())
                            {
                                string display = line;
                                var t = line.TrimStart();
                                if (t.StartsWith("[+]") || t.StartsWith("[~]") || t.StartsWith("[X]"))
                                {
                                    string mark = t.Substring(0, 3);
                                    string trait = t.Length > 3 ? t.Substring(3).Trim() : "";
                                    display = mark + " " + (string.IsNullOrEmpty(trait) ? trait : _loc.T(trait));
                                }
                                ImGui.TextUnformatted(display);
                            }
                        }

                        if (_pawnAgent != null)
                        {
                            Pawn? any = null;
                            foreach (var p in _pawnAgent.Pawns) { if (p.IsAlive) { any = p; break; } }
                            if (any != null)
                            {
                                ImGui.Separator();
                                ImGui.Text($"{_loc.T("Id")} {any.Id} {_loc.T("Gen")} {any.Generation} {_loc.T("Energy")} {any.EnergyPercent:P0}");
                                ImGui.Text($"{_loc.T("Legs")} {any.Genome.LegCount} {_loc.T("Water")} {any.Genome.WaterAffinity:F2} {_loc.T("Size")} {any.Genome.Size:F2}");
                                ImGui.TextUnformatted(any.Genome.DNA);
                            }
                        }

                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem(_loc.T("Planet")))
                    {
                        float t = _config.Temperature;
                        if (ImGui.SliderFloat(_loc.T("Temperature"), ref t, 0f, 1f, "%.2f")) { _config.Temperature = t; UpdateTitle(); SaveConfig(); }
                        ImGui.TextDisabled(_loc.T("TemperatureHint"));
                        float a = _config.Atmosphere;
                        if (ImGui.SliderFloat(_loc.T("Atmosphere"), ref a, 0f, 1f, "%.2f")) { _config.Atmosphere = a; UpdateTitle(); SaveConfig(); }
                        ImGui.TextDisabled(_loc.T("AtmosphereHint"));
                        float g = _config.GeologicActivity;
                        if (ImGui.SliderFloat(_loc.T("Geologic"), ref g, 0f, 2f, "%.2f")) { _config.GeologicActivity = g; UpdateTitle(); SaveConfig(); RequestPlanetRegenerate(false); }
                        float f = _config.NoiseFrequency;
                        if (ImGui.SliderFloat(_loc.T("Noise Freq"), ref f, 0.01f, 2f, "%.2f")) { _config.NoiseFrequency = f; UpdateTitle(); SaveConfig(); RequestPlanetRegenerate(false); }
                        int o = _config.NoiseOctaves;
                        if (ImGui.SliderInt(_loc.T("Octaves"), ref o, 1, 8)) { _config.NoiseOctaves = o; SaveConfig(); RequestPlanetRegenerate(false); }

                        float d = _config.Density;
                        if (ImGui.SliderFloat(_loc.T("Density"), ref d, 0.1f, 5f, "%.2f")) { _config.Density = d; SaveConfig(); }
                        float r = _config.Radius;
                        if (ImGui.SliderFloat(_loc.T("Radius"), ref r, 1f, 20f, "%.2f")) { _config.Radius = r; SaveConfig(); CreateSphereGeometry(); CreateStars(_config.StarCount, _starSeed, _config.Radius); RequestPlanetRegenerate(false); }
                        float disp = _config.DisplacementScale;
                        if (ImGui.SliderFloat(_loc.T("Displace"), ref disp, 0f, 2f, "%.2f")) { _config.DisplacementScale = disp; SaveConfig(); }
                        int seg = _config.Segments;
                        if (ImGui.SliderInt(_loc.T("Segments"), ref seg, 32, 512)) { _config.Segments = seg; SaveConfig(); CreateSphereGeometry(); }
                        int sc = _config.StarCount;
                        if (ImGui.SliderInt(_loc.T("StarCount"), ref sc, 50, 5000)) { _config.StarCount = sc; SaveConfig(); CreateStars(_config.StarCount, _starSeed, _config.Radius); }

                        float ms = _config.MutationSpeed;
                        if (ImGui.SliderFloat(_loc.T("MutationSpeed"), ref ms, 1f, 60f, "%.1f")) { _config.MutationSpeed = ms; SaveConfig(); }
                        float ami = _config.AutoMutationInterval;
                        if (ImGui.SliderFloat(_loc.T("AutoMutationInterval"), ref ami, 0f, 300f, "%.1f")) { _config.AutoMutationInterval = ami; SaveConfig(); }
                        int mf = _config.MutationFields;
                        if (ImGui.SliderInt(_loc.T("MutationFields"), ref mf, 1, 7)) { _config.MutationFields = mf; SaveConfig(); }

                        if (ImGui.Button(_loc.T("Regenerate"))) { RequestPlanetRegenerate(false); SaveConfig(); }
                        ImGui.SameLine();
                        if (ImGui.Button(_loc.T("Mutate"))) { ApplyPlanetMutation(); }

                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                ImGui.End();
            }

            if (_showConsole)
            {
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(650, 420), ImGuiCond.FirstUseEver);
                if (ImGui.Begin(_loc.T("Console"), ref _showConsole, ImGuiWindowFlags.NoCollapse))
                {
                    if (ImGui.Button(_loc.T("Clear"))) GameLog.Clear();
                    ImGui.SameLine();
                    string logPath = Path.Combine(AppContext.BaseDirectory ?? ".", "spacedna.log");
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.7f, 0.8f, 1f), logPath);
                    ImGui.Separator();
                    ImGui.BeginChild("##log", new System.Numerics.Vector2(0, -4), ImGuiChildFlags.None, ImGuiWindowFlags.AlwaysVerticalScrollbar);
                    foreach (var line in GameLog.GetLines()) ImGui.TextUnformatted(line);
                    ImGui.EndChild();
                }
                ImGui.End();
            }

            if (_showMenu)
            {
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(ClientSize.X / 2f - 175, ClientSize.Y / 2f - 200), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(350, 420), ImGuiCond.Always);
                if (ImGui.Begin(_loc.T("Menu"), ref _showMenu, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
                {
                    ImGui.Text($"Version {SpaceDNA.GameVersion.Current}");
                    float vol = _masterVolume;
                    if (ImGui.SliderFloat(_loc.T("Volume"), ref vol, 0f, 1f, "%.2f")) { _masterVolume = vol; SaveConfig(); }
                    bool ae = _audioEnabled;
                    if (ImGui.Checkbox(_loc.T("Audio"), ref ae)) _audioEnabled = ae;
                    if (_audio == null) ImGui.TextDisabled(_loc.T("AudioUnavailable"));
                    bool con = _showConsole;
                    if (ImGui.Checkbox(_loc.T("Console"), ref con)) _showConsole = con;

                    ImGui.Separator();
                    ImGui.TextUnformatted(_loc.T("Language"));
                    int langIdx = _language == Language.Ru ? 0 : 1;
                    string[] langs = new[] { _loc.T("Russian"), _loc.T("English") };
                    if (ImGui.Combo("##lang", ref langIdx, langs, langs.Length))
                    {
                        _language = langIdx == 0 ? Language.Ru : Language.En;
                        _loc.SetLanguage(_language);
                        UpdateTitle();
                    }

                    if (ImGui.Button(_loc.T("Exit"), new System.Numerics.Vector2(-1, 30))) Close();
                }
                ImGui.End();
            }

            _imGuiController.Render();
        }

        SwapBuffers();
    }

    protected override void OnUnload()
    {
        base.OnUnload();
        if (_sphereVao != 0) { GL.DeleteVertexArray(_sphereVao); GL.DeleteBuffer(_sphereVbo); GL.DeleteBuffer(_sphereEbo); }
        if (_sphereProgram != 0) GL.DeleteProgram(_sphereProgram);
        if (_starVao != 0) { GL.DeleteVertexArray(_starVao); GL.DeleteBuffer(_starVbo); }
        if (_starProgram != 0) GL.DeleteProgram(_starProgram);
        if (_pawnVao != 0) { GL.DeleteVertexArray(_pawnVao); GL.DeleteBuffer(_pawnVbo); }
        if (_pawnProgram != 0) GL.DeleteProgram(_pawnProgram);
        if (_pawn3DVao != 0) { GL.DeleteVertexArray(_pawn3DVao); GL.DeleteBuffer(_pawn3DVbo); GL.DeleteBuffer(_pawn3DEbo); }
        if (_pawn3DProgram != 0) GL.DeleteProgram(_pawn3DProgram);
        if (_heightmapTextureA != 0) GL.DeleteTexture(_heightmapTextureA);
        if (_heightmapTextureB != 0) GL.DeleteTexture(_heightmapTextureB);
        _imGuiController?.Dispose();
        try { _audio?.Dispose(); } catch { }
        GameLog.Shutdown();
    }
}
