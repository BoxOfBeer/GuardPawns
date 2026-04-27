using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.IO;
using System.Runtime.InteropServices;

// ImDrawVert layout: pos(8) + uv(8) + col(4) = 20 bytes
// Используем константу, т.к. ImDrawVert.SizeInBytes отсутствует в ImGui.NET 1.91+
public class ImGuiController : IDisposable
{
    private const int ImDrawVertSize = 20; // sizeof(ImDrawVert)

    private bool _frameBegun;
    private int _vertexBuffer;
    private int _vertexBufferSize;
    private int _indexBuffer;
    private int _indexBufferSize;
    private int _vertexArray;
    private int _fontTexture;
    private int _shader;
    private int _shaderFontTextureLocation;
    private int _shaderProjectionLocation;

    private Vector2i _windowSize;

    public ImGuiController(int width, int height)
    {
        _windowSize = new Vector2i(width, height);

        IntPtr context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

        // Шрифт с кириллицей — перебираем пути для кроссплатформенности
        string[] fontCandidates = new[]
        {
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\tahoma.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
        };

        bool fontLoaded = false;
        foreach (var fontPath in fontCandidates)
        {
            if (File.Exists(fontPath))
            {
                try
                {
                    io.Fonts.AddFontFromFileTTF(fontPath, 20f, null, io.Fonts.GetGlyphRangesCyrillic());
                    fontLoaded = true;
                    Console.WriteLine($"ImGui: Font loaded from {fontPath}");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ImGui: Failed to load font {fontPath}: {ex.Message}");
                }
            }
        }
        if (!fontLoaded)
        {
            io.Fonts.AddFontDefault();
            Console.WriteLine("ImGui: Using default font");
        }

        ImGui.StyleColorsDark();
        CreateDeviceResources();
        
        // Установить DisplaySize перед первым NewFrame
        var io2 = ImGui.GetIO();
        io2.DisplaySize = new System.Numerics.Vector2(width, height);
        io2.DeltaTime = 1f / 60f;
        
        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void WindowResized(int width, int height)
    {
        _windowSize = new Vector2i(width, height);
    }

    public void Update(GameWindow window, FrameEventArgs e)
    {
        if (_frameBegun)
        {
            ImGui.EndFrame();
        }

        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(_windowSize.X, _windowSize.Y);
        io.DeltaTime = (float)e.Time;

        UpdateMouse(window.MouseState);
        UpdateKeyboard(window.KeyboardState);

        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void Render()
    {
        if (!_frameBegun) return;
        _frameBegun = false;
        ImGui.Render();
        RenderImDrawData(ImGui.GetDrawData());
    }

    private void UpdateMouse(MouseState mouse)
    {
        var io = ImGui.GetIO();
        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseWheelEvent(0f, mouse.ScrollDelta.Y);
        io.AddMouseButtonEvent(0, mouse.IsButtonDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, mouse.IsButtonDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, mouse.IsButtonDown(MouseButton.Middle));
    }

    private void UpdateKeyboard(KeyboardState keyboard)
    {
        var io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.ModCtrl,  keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift)   || keyboard.IsKeyDown(Keys.RightShift));
        io.AddKeyEvent(ImGuiKey.ModAlt,   keyboard.IsKeyDown(Keys.LeftAlt)     || keyboard.IsKeyDown(Keys.RightAlt));
        io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyDown(Keys.LeftSuper)   || keyboard.IsKeyDown(Keys.RightSuper));

        io.AddKeyEvent(ImGuiKey.Tab,       keyboard.IsKeyDown(Keys.Tab));
        io.AddKeyEvent(ImGuiKey.LeftArrow, keyboard.IsKeyDown(Keys.Left));
        io.AddKeyEvent(ImGuiKey.RightArrow,keyboard.IsKeyDown(Keys.Right));
        io.AddKeyEvent(ImGuiKey.UpArrow,   keyboard.IsKeyDown(Keys.Up));
        io.AddKeyEvent(ImGuiKey.DownArrow, keyboard.IsKeyDown(Keys.Down));
        io.AddKeyEvent(ImGuiKey.PageUp,    keyboard.IsKeyDown(Keys.PageUp));
        io.AddKeyEvent(ImGuiKey.PageDown,  keyboard.IsKeyDown(Keys.PageDown));
        io.AddKeyEvent(ImGuiKey.Home,      keyboard.IsKeyDown(Keys.Home));
        io.AddKeyEvent(ImGuiKey.End,       keyboard.IsKeyDown(Keys.End));
        io.AddKeyEvent(ImGuiKey.Insert,    keyboard.IsKeyDown(Keys.Insert));
        io.AddKeyEvent(ImGuiKey.Delete,    keyboard.IsKeyDown(Keys.Delete));
        io.AddKeyEvent(ImGuiKey.Backspace, keyboard.IsKeyDown(Keys.Backspace));
        io.AddKeyEvent(ImGuiKey.Space,     keyboard.IsKeyDown(Keys.Space));
        io.AddKeyEvent(ImGuiKey.Enter,     keyboard.IsKeyDown(Keys.Enter));
        io.AddKeyEvent(ImGuiKey.Escape,    keyboard.IsKeyDown(Keys.Escape));
        io.AddKeyEvent(ImGuiKey.A,         keyboard.IsKeyDown(Keys.A));
        io.AddKeyEvent(ImGuiKey.C,         keyboard.IsKeyDown(Keys.C));
        io.AddKeyEvent(ImGuiKey.V,         keyboard.IsKeyDown(Keys.V));
        io.AddKeyEvent(ImGuiKey.X,         keyboard.IsKeyDown(Keys.X));
        io.AddKeyEvent(ImGuiKey.Y,         keyboard.IsKeyDown(Keys.Y));
        io.AddKeyEvent(ImGuiKey.Z,         keyboard.IsKeyDown(Keys.Z));
    }

    private void CreateDeviceResources()
    {
        // Vertex array
        _vertexArray = GL.GenVertexArray();
        GL.BindVertexArray(_vertexArray);

        // Vertex buffer
        _vertexBuffer = GL.GenBuffer();
        _vertexBufferSize = 10000;
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        // Index buffer
        _indexBuffer = GL.GenBuffer();
        _indexBufferSize = 2000;
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        // Vertex attributes: Position (vec2 @ 0), UV (vec2 @ 8), Color (ubyte4 @ 16)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, ImDrawVertSize, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, ImDrawVertSize, 8);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, ImDrawVertSize, 16);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

        // Shader
        _shader = CreateShaderProgram();
        _shaderFontTextureLocation = GL.GetUniformLocation(_shader, "Texture");
        _shaderProjectionLocation = GL.GetUniformLocation(_shader, "ProjectionMatrix");

        // Font texture
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);
        _fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }

    private int CreateShaderProgram()
    {
        string vertexSrc = @"
            #version 330 core
            layout(location = 0) in vec2 Position;
            layout(location = 1) in vec2 UV;
            layout(location = 2) in vec4 Color;
            uniform mat4 ProjectionMatrix;
            out vec2 FragUV;
            out vec4 FragColor;
            void main()
            {
                FragUV = UV;
                FragColor = Color;
                gl_Position = ProjectionMatrix * vec4(Position, 0, 1);
            }";

        string fragmentSrc = @"
            #version 330 core
            in vec2 FragUV;
            in vec4 FragColor;
            out vec4 OutColor;
            uniform sampler2D Texture;
            void main()
            {
                OutColor = FragColor * texture(Texture, FragUV);
            }";

        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, vertexSrc);
        GL.CompileShader(vs);
        GL.GetShader(vs, ShaderParameter.CompileStatus, out int vsOk);
        if (vsOk == 0)
            throw new Exception("ImGui vertex shader: " + GL.GetShaderInfoLog(vs));

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, fragmentSrc);
        GL.CompileShader(fs);
        GL.GetShader(fs, ShaderParameter.CompileStatus, out int fsOk);
        if (fsOk == 0)
            throw new Exception("ImGui fragment shader: " + GL.GetShaderInfoLog(fs));

        int program = GL.CreateProgram();
        GL.AttachShader(program, vs);
        GL.AttachShader(program, fs);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkOk);
        if (linkOk == 0)
            throw new Exception("ImGui shader link: " + GL.GetProgramInfoLog(program));

        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
        return program;
    }

    private void RenderImDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0) return;

        GL.GetInteger(GetPName.CurrentProgram, out int prevProgram);
        
        // Сохраняем предыдущее состояние
        bool prevBlend = GL.IsEnabled(EnableCap.Blend);
        bool prevCullFace = GL.IsEnabled(EnableCap.CullFace);
        bool prevDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool prevScissorTest = GL.IsEnabled(EnableCap.ScissorTest);
        int[] prevViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, prevViewport);

        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFuncSeparate(
            BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
            BlendingFactorSrc.One,      BlendingFactorDest.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);
        GL.Viewport(0, 0, _windowSize.X, _windowSize.Y);

        GL.UseProgram(_shader);
        GL.Uniform1(_shaderFontTextureLocation, 0);

        var ortho = Matrix4.CreateOrthographicOffCenter(0, _windowSize.X, _windowSize.Y, 0, -1, 1);
        GL.UniformMatrix4(_shaderProjectionLocation, false, ref ortho);

        GL.BindVertexArray(_vertexArray);
        GL.ActiveTexture(TextureUnit.Texture0);

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            int vtxSize = cmdList.VtxBuffer.Size * ImDrawVertSize;
            int idxSize = cmdList.IdxBuffer.Size * sizeof(ushort);

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            if (vtxSize > _vertexBufferSize)
            {
                _vertexBufferSize = vtxSize;
                GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vtxSize, cmdList.VtxBuffer.Data);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            if (idxSize > _indexBufferSize)
            {
                _indexBufferSize = idxSize;
                GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }
            GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, idxSize, cmdList.IdxBuffer.Data);

            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                ImDrawCmdPtr cmd = cmdList.CmdBuffer[i];
                if (cmd.UserCallback != IntPtr.Zero)
                    continue;

                // Поддержка пользовательских текстур
                GL.BindTexture(TextureTarget.Texture2D, (int)cmd.TextureId);

                GL.Scissor(
                    (int)cmd.ClipRect.X,
                    (int)(_windowSize.Y - cmd.ClipRect.W),
                    (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                    (int)(cmd.ClipRect.W - cmd.ClipRect.Y));

                GL.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    (int)cmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (IntPtr)(cmd.IdxOffset * sizeof(ushort)),
                    (int)cmd.VtxOffset);
            }
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.UseProgram(prevProgram);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        
        // Восстанавливаем предыдущее состояние
        if (prevBlend) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
        if (prevCullFace) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
        if (prevDepthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
        if (prevScissorTest) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
        GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);
    }

    public void Dispose()
    {
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteBuffer(_indexBuffer);
        GL.DeleteVertexArray(_vertexArray);
        GL.DeleteTexture(_fontTexture);
        GL.DeleteProgram(_shader);
        ImGui.DestroyContext();
    }
}
