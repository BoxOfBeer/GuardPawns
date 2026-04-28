using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SpaceDNA.Core;

public partial class Game : GameWindow
{
    private readonly PlanetSurface _surface = new();
    private PawnAgent _pawnAgent = null!;
    private PlanetConfig _config = new();

    private ImGuiController? _imGui;

    private int _meshProgram;
    private int _pointProgram;

    private MeshBuffers _terrainMesh;
    private MeshBuffers _atmosphereMesh;
    private MeshBuffers _cloudMesh;
    private PointsBuffer _stars;
    private PointsBuffer _pawns;

    private readonly List<Vector3> _starDirections = new();

    private float _cameraDistance = 16f;
    private float _yaw = -0.8f;
    private float _pitch = 0.45f;
    private Vector2 _lastMouse;
    private bool _dragging;

    private bool _showPawns = true;

    private const int PawnCount = 20;

    public Game(GameWindowSettings gameSettings, NativeWindowSettings nativeSettings) : base(gameSettings, nativeSettings)
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        GL.ClearColor(0.015f, 0.02f, 0.04f, 1f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _imGui = new ImGuiController(ClientSize.X, ClientSize.Y);
        _meshProgram = BuildProgram(VertexShaderSource, FragmentShaderSource);
        _pointProgram = BuildProgram(PointVertexShaderSource, PointFragmentShaderSource);

        _config = PlanetConfig.Load(Path.Combine(AppContext.BaseDirectory ?? ".", "planet_config.json")) ?? new PlanetConfig();
        _config.Radius = MathHelper.Clamp(_config.Radius, 2f, 15f);
        _config.DisplacementScale = MathHelper.Clamp(_config.DisplacementScale, 0f, 2f);

        _pawnAgent = new PawnAgent(_surface);
        RegeneratePlanet();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        _imGui?.WindowResized(e.Width, e.Height);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        if (!IsFocused)
            return;

        var input = KeyboardState;
        if (input.IsKeyDown(Keys.Escape))
            Close();

        var mouse = MouseState;
        if (mouse.IsButtonDown(MouseButton.Left))
        {
            if (!_dragging)
            {
                _dragging = true;
                _lastMouse = mouse.Position;
            }
            else
            {
                Vector2 delta = mouse.Position - _lastMouse;
                _lastMouse = mouse.Position;
                _yaw += delta.X * 0.005f;
                _pitch = MathHelper.Clamp(_pitch + delta.Y * 0.005f, -1.35f, 1.35f);
            }
        }
        else
        {
            _dragging = false;
        }

        _cameraDistance = MathHelper.Clamp(_cameraDistance - mouse.ScrollDelta.Y * 0.6f, 7f, 80f);

        _pawnAgent.Update((float)args.Time);
        UpdatePawnBuffer();
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Matrix4 view = BuildViewMatrix();
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), Size.X / (float)Size.Y, 0.1f, 500f);

        DrawStars(view, projection);
        DrawMesh(_terrainMesh, view, projection, new Vector4(0.24f, 0.62f, 0.31f, 1f));
        DrawMesh(_atmosphereMesh, view, projection, new Vector4(0.44f, 0.62f, 1f, 0.18f), cullFace: false);
        DrawMesh(_cloudMesh, view, projection, new Vector4(0.92f, 0.94f, 0.97f, 0.34f), cullFace: false);

        if (_showPawns)
            DrawPawns(view, projection);

        DrawImGui(args);
        SwapBuffers();
    }

    private void DrawImGui(FrameEventArgs args)
    {
        if (_imGui == null)
            return;

        _imGui.Update(this, args);

        ImGui.SetNextWindowPos(new System.Numerics.Vector2(14, 14), ImGuiCond.Once);
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(360, 240), ImGuiCond.Once);
        ImGui.Begin("Planet Controls", ImGuiWindowFlags.NoCollapse);

        float radius = _config.Radius;
        if (ImGui.SliderFloat("Radius", ref radius, 2f, 15f, "%.2f"))
        {
            _config.Radius = radius;
            RebuildMeshes();
        }

        float displacement = _config.DisplacementScale;
        if (ImGui.SliderFloat("Displacement", ref displacement, 0f, 2f, "%.2f"))
        {
            _config.DisplacementScale = displacement;
            RebuildMeshes();
        }

        int seed = _config.Seed;
        ImGui.InputInt("Seed", ref seed);
        _config.Seed = Math.Max(1, seed);

        if (ImGui.Button("Regenerate"))
            RegeneratePlanet();

        ImGui.SameLine();
        ImGui.Checkbox("Show pawns", ref _showPawns);

        ImGui.Text($"Terrain radius range: {_surface.MinSurfaceRadius:F2} .. {_surface.MaxSurfaceRadius:F2}");
        ImGui.Text($"Atmosphere radius: {_surface.GetAtmosphereRadius():F2}");
        ImGui.Text($"Cloud radius: {_surface.GetCloudRadius():F2}");

        ImGui.End();

        _imGui.Render();
    }

    private void RegeneratePlanet()
    {
        _surface.SetPlanet(_config.Radius, _config.DisplacementScale);
        _surface.Generate(_config.Seed, 256);

        _pawnAgent.InitializePopulation(PawnCount);
        CreateStars(_config.StarCount, _config.Seed + 7001);

        RebuildMeshes();
        LogStartupDiagnostics();
    }

    private void RebuildMeshes()
    {
        _surface.SetPlanet(_config.Radius, _config.DisplacementScale);
        CreateTerrainMesh(Math.Clamp(_config.Segments, 24, 196));
        CreateLayerMesh(ref _atmosphereMesh, _surface.GetAtmosphereRadius(), 72);
        CreateLayerMesh(ref _cloudMesh, _surface.GetCloudRadius(), 72);
        UpdatePawnBuffer();
        CreateStarsBuffer();
    }

    private void CreateTerrainMesh(int segments)
    {
        DeleteMesh(ref _terrainMesh);
        BuildSphere(segments, segments, dir => _surface.GetSurfacePoint(dir), out _terrainMesh);
    }

    private void CreateLayerMesh(ref MeshBuffers mesh, float radius, int segments)
    {
        DeleteMesh(ref mesh);
        BuildSphere(segments, segments, dir => _surface.GetPointAtRadius(dir, radius), out mesh);
    }

    private void BuildSphere(int latSegments, int lonSegments, Func<Vector3, Vector3> pointSampler, out MeshBuffers mesh)
    {
        var vertices = new List<float>();
        var indices = new List<uint>();

        for (int lat = 0; lat <= latSegments; lat++)
        {
            float v = lat / (float)latSegments;
            float phi = MathF.PI * v;
            float y = MathF.Cos(phi);
            float r = MathF.Sin(phi);

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float u = lon / (float)lonSegments;
                float theta = MathF.PI * 2f * u;
                var dir = new Vector3(r * MathF.Cos(theta), y, r * MathF.Sin(theta));
                Vector3 point = pointSampler(dir);
                Vector3 normal = point.LengthSquared > 0.0001f ? Vector3.Normalize(point) : Vector3.UnitY;

                vertices.Add(point.X);
                vertices.Add(point.Y);
                vertices.Add(point.Z);
                vertices.Add(normal.X);
                vertices.Add(normal.Y);
                vertices.Add(normal.Z);
            }
        }

        int stride = lonSegments + 1;
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                uint i0 = (uint)(lat * stride + lon);
                uint i1 = i0 + 1;
                uint i2 = (uint)((lat + 1) * stride + lon);
                uint i3 = i2 + 1;

                indices.Add(i0); indices.Add(i2); indices.Add(i1);
                indices.Add(i1); indices.Add(i2); indices.Add(i3);
            }
        }

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        mesh = new MeshBuffers(vao, vbo, ebo, indices.Count);
    }

    private void CreateStars(int count, int seed)
    {
        _starDirections.Clear();
        var rnd = new Random(seed);
        int safeCount = Math.Clamp(count, 50, 5000);
        for (int i = 0; i < safeCount; i++)
        {
            float z = (float)(rnd.NextDouble() * 2.0 - 1.0);
            float ang = (float)(rnd.NextDouble() * Math.PI * 2.0);
            float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
            _starDirections.Add(new Vector3(r * MathF.Cos(ang), z, r * MathF.Sin(ang)));
        }
    }

    private void CreateStarsBuffer()
    {
        DeletePoints(ref _stars);
        float starRadius = _surface.GetCloudRadius() * 6f;
        var data = new float[_starDirections.Count * 3];
        for (int i = 0; i < _starDirections.Count; i++)
        {
            Vector3 p = _surface.GetPointAtRadius(_starDirections[i], starRadius);
            data[i * 3 + 0] = p.X;
            data[i * 3 + 1] = p.Y;
            data[i * 3 + 2] = p.Z;
        }

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.DynamicDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        _stars = new PointsBuffer(vao, vbo, _starDirections.Count);
    }

    private void UpdatePawnBuffer()
    {
        DeletePoints(ref _pawns);
        var pawns = _pawnAgent.Pawns;
        var data = new float[pawns.Count * 3];
        for (int i = 0; i < pawns.Count; i++)
        {
            Vector3 p = _pawnAgent.GetWorldPosition(pawns[i]);
            data[i * 3 + 0] = p.X;
            data[i * 3 + 1] = p.Y;
            data[i * 3 + 2] = p.Z;
        }

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.DynamicDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        _pawns = new PointsBuffer(vao, vbo, pawns.Count);
    }

    private void DrawMesh(MeshBuffers mesh, Matrix4 view, Matrix4 projection, Vector4 color, bool cullFace = true)
    {
        if (mesh.Vao == 0)
            return;

        if (cullFace) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);

        GL.UseProgram(_meshProgram);
        GL.UniformMatrix4(GL.GetUniformLocation(_meshProgram, "uModel"), false, ref Matrix4.Identity);
        GL.UniformMatrix4(GL.GetUniformLocation(_meshProgram, "uView"), false, ref view);
        GL.UniformMatrix4(GL.GetUniformLocation(_meshProgram, "uProj"), false, ref projection);
        GL.Uniform4(GL.GetUniformLocation(_meshProgram, "uColor"), color);
        GL.Uniform3(GL.GetUniformLocation(_meshProgram, "uLightDir"), new Vector3(0.6f, 0.8f, 0.2f));

        GL.BindVertexArray(mesh.Vao);
        GL.DrawElements(PrimitiveType.Triangles, mesh.IndexCount, DrawElementsType.UnsignedInt, 0);
    }

    private void DrawStars(Matrix4 view, Matrix4 projection)
    {
        DrawPoints(_stars, view, projection, new Vector4(1f, 1f, 1f, 1f), 2.2f);
    }

    private void DrawPawns(Matrix4 view, Matrix4 projection)
    {
        DrawPoints(_pawns, view, projection, new Vector4(1f, 0.9f, 0.2f, 1f), 8f);
    }

    private void DrawPoints(PointsBuffer buffer, Matrix4 view, Matrix4 projection, Vector4 color, float size)
    {
        if (buffer.Vao == 0 || buffer.Count <= 0)
            return;

        GL.UseProgram(_pointProgram);
        GL.UniformMatrix4(GL.GetUniformLocation(_pointProgram, "uView"), false, ref view);
        GL.UniformMatrix4(GL.GetUniformLocation(_pointProgram, "uProj"), false, ref projection);
        GL.Uniform4(GL.GetUniformLocation(_pointProgram, "uColor"), color);
        GL.Uniform1(GL.GetUniformLocation(_pointProgram, "uPointSize"), size);

        GL.BindVertexArray(buffer.Vao);
        GL.DrawArrays(PrimitiveType.Points, 0, buffer.Count);
    }

    private Matrix4 BuildViewMatrix()
    {
        Vector3 forward = new(
            MathF.Cos(_pitch) * MathF.Cos(_yaw),
            MathF.Sin(_pitch),
            MathF.Cos(_pitch) * MathF.Sin(_yaw));

        Vector3 eye = -forward * _cameraDistance;
        return Matrix4.LookAt(eye, Vector3.Zero, Vector3.UnitY);
    }

    private void LogStartupDiagnostics()
    {
        float firstPawnRadius = _pawnAgent.Pawns.Count > 0
            ? _pawnAgent.GetWorldPosition(_pawnAgent.Pawns[0]).Length
            : 0f;

        string msg =
            $"[PlanetStartup] radius={_surface.Radius:F3}, displacementScale={_surface.DisplacementScale:F3}, " +
            $"height01(min/max)={_surface.MinHeight01:F3}/{_surface.MaxHeight01:F3}, " +
            $"surfaceRadius(min/max)={_surface.MinSurfaceRadius:F3}/{_surface.MaxSurfaceRadius:F3}, " +
            $"atmosphereRadius={_surface.GetAtmosphereRadius():F3}, cloudRadius={_surface.GetCloudRadius():F3}, " +
            $"firstPawnRadius={firstPawnRadius:F3}";

        GameLog.Log(msg);
        Console.WriteLine(msg);
    }

    protected override void OnUnload()
    {
        base.OnUnload();

        DeleteMesh(ref _terrainMesh);
        DeleteMesh(ref _atmosphereMesh);
        DeleteMesh(ref _cloudMesh);
        DeletePoints(ref _stars);
        DeletePoints(ref _pawns);

        if (_meshProgram != 0) GL.DeleteProgram(_meshProgram);
        if (_pointProgram != 0) GL.DeleteProgram(_pointProgram);

        _imGui?.Dispose();
    }

    private static void DeleteMesh(ref MeshBuffers mesh)
    {
        if (mesh.Vao != 0) GL.DeleteVertexArray(mesh.Vao);
        if (mesh.Vbo != 0) GL.DeleteBuffer(mesh.Vbo);
        if (mesh.Ebo != 0) GL.DeleteBuffer(mesh.Ebo);
        mesh = default;
    }

    private static void DeletePoints(ref PointsBuffer points)
    {
        if (points.Vao != 0) GL.DeleteVertexArray(points.Vao);
        if (points.Vbo != 0) GL.DeleteBuffer(points.Vbo);
        points = default;
    }

    private static int BuildProgram(string vertexSource, string fragmentSource)
    {
        int v = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(v, vertexSource);
        GL.CompileShader(v);
        GL.GetShader(v, ShaderParameter.CompileStatus, out int vOk);
        if (vOk == 0) throw new InvalidOperationException(GL.GetShaderInfoLog(v));

        int f = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(f, fragmentSource);
        GL.CompileShader(f);
        GL.GetShader(f, ShaderParameter.CompileStatus, out int fOk);
        if (fOk == 0) throw new InvalidOperationException(GL.GetShaderInfoLog(f));

        int p = GL.CreateProgram();
        GL.AttachShader(p, v);
        GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int pOk);
        if (pOk == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(p));

        GL.DetachShader(p, v);
        GL.DetachShader(p, f);
        GL.DeleteShader(v);
        GL.DeleteShader(f);
        return p;
    }

    private readonly record struct MeshBuffers(int Vao, int Vbo, int Ebo, int IndexCount);
    private readonly record struct PointsBuffer(int Vao, int Vbo, int Count);

    private const string VertexShaderSource = """
#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;

out vec3 vNormal;
out vec3 vWorldPos;

void main()
{
    vec4 world = uModel * vec4(aPosition, 1.0);
    vWorldPos = world.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    gl_Position = uProj * uView * world;
}
""";

    private const string FragmentShaderSource = """
#version 330 core
in vec3 vNormal;
in vec3 vWorldPos;

uniform vec4 uColor;
uniform vec3 uLightDir;

out vec4 fragColor;

void main()
{
    vec3 normal = normalize(vNormal);
    float diff = max(dot(normal, normalize(uLightDir)), 0.0);
    float ambient = 0.35;
    float light = ambient + diff * 0.65;
    fragColor = vec4(uColor.rgb * light, uColor.a);
}
""";

    private const string PointVertexShaderSource = """
#version 330 core
layout (location = 0) in vec3 aPosition;

uniform mat4 uView;
uniform mat4 uProj;
uniform float uPointSize;

void main()
{
    gl_Position = uProj * uView * vec4(aPosition, 1.0);
    gl_PointSize = uPointSize;
}
""";

    private const string PointFragmentShaderSource = """
#version 330 core
uniform vec4 uColor;
out vec4 fragColor;

void main()
{
    fragColor = uColor;
}
""";
}
