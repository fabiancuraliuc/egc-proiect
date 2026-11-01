using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

public class Game : GameWindow
{
    private readonly Camera _camera = new();
    private Shader _shader = null!;
    private readonly List<SceneObject> _scene = new();
    private readonly List<Transform2D> _initialTransforms = new();

    private int _selected;
    private bool _wireframe;

    private readonly string _baseTitle;
    private double _secondTimer;
    private int _frames, _lastFrames;
    private bool _titleDirty = true;

    // T4: Pivotul pentru rotație
    private Vector2? _pivot;

    // T5: Satelitul și unghiul orbitei sale
    private SceneObject _satellite = null!;
    private float _satelliteOrbitAngle = 0.0f;

    public Game(int width, int height, string title)
        : base(GameWindowSettings.Default,
               new NativeWindowSettings
               {
                   ClientSize = new Vector2i(width, height),
                   Title = title,
                   APIVersion = new Version(3, 3),
                   Profile = ContextProfile.Core,
                   Flags = ContextFlags.ForwardCompatible
               })
    {
        _baseTitle = title;
        VSync = VSyncMode.On;
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        Console.WriteLine($"OpenGL  : {GL.GetString(StringName.Version)}");
        Console.WriteLine($"Renderer: {GL.GetString(StringName.Renderer)}");

        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        _camera.Resize(ClientSize.X, ClientSize.Y);

        _shader = Shader.FromFiles("Shaders/shader.vert", "Shaders/shader.frag");

        AddObject("pătrat", Shapes.Quad(),
                  new Transform2D { Position = new Vector2(-1.8f, -1.0f), Depth = -3.0f });
        AddObject("triunghi", Shapes.Triangle(),
                  new Transform2D { Position = new Vector2(-1.2f, 1.0f) });
        AddObject("hexagon", Shapes.RegularPolygon(6, 0.5f),
                  new Transform2D { Position = new Vector2(2.0f, 1.0f) });
        AddObject("cerc", Shapes.RegularPolygon(64, 0.5f),
                  new Transform2D { Position = new Vector2(0.8f, -0.6f), Depth = 2.0f });

        // T5: Inițializare satelit (nu îl adăugăm în _scene pentru a nu fi selectat cu Tab)
        _satellite = new SceneObject("satelit", Shapes.Triangle(), new Transform2D());
        _satellite.Tint = Color4.Cyan;
    }

    private void AddObject(string name, Mesh mesh, Transform2D transform)
    {
        _scene.Add(new SceneObject(name, mesh, transform));
        _initialTransforms.Add(transform.Clone());
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        _camera.Resize(e.Width, e.Height);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        if (KeyboardState.IsKeyDown(Keys.Escape)) Close();

        float dt = (float)args.Time;
        SceneObject sel = _scene[_selected];

        // T2: Multiplicator pentru tasta Shift (mișcare de 3 ori mai rapidă)
        float speedMultiplier = KeyboardState.IsKeyDown(Keys.LeftShift) ? 3.0f : 1.0f;

        float moveSpeed = 2.0f * speedMultiplier;
        if (KeyboardState.IsKeyDown(Keys.Left)) sel.Transform.Position.X -= moveSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.Right)) sel.Transform.Position.X += moveSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.Down)) sel.Transform.Position.Y -= moveSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.Up)) sel.Transform.Position.Y += moveSpeed * dt;

        // T2: Q / E rotesc obiectul (90 grade / secundă)
        float rotSpeed = 90.0f * speedMultiplier * dt;
        if (KeyboardState.IsKeyDown(Keys.Q)) sel.Transform.Rotation += rotSpeed;
        if (KeyboardState.IsKeyDown(Keys.E)) sel.Transform.Rotation -= rotSpeed;

        // T2: Z / X îl scalează (factor 1.5 pe secundă)
        // T2: Scalarea se face prin înmulțire deoarece este o transformare geometrică proporțională relativ la mărimea curentă. 
        // Adunarea ar deforma scara în mod liniar sau ar putea duce la valori negative nedorite (răsturnând obiectul).
        float scaleFactor = MathF.Pow(1.5f, dt * speedMultiplier);
        if (KeyboardState.IsKeyDown(Keys.X)) sel.Transform.Scale *= scaleFactor; // mărește
        if (KeyboardState.IsKeyDown(Keys.Z)) sel.Transform.Scale /= scaleFactor; // micșorează

        sel.Transform.Scale.X = Math.Clamp(sel.Transform.Scale.X, 0.1f, 10.0f);
        sel.Transform.Scale.Y = Math.Clamp(sel.Transform.Scale.Y, 0.1f, 10.0f);

        if (KeyboardState.IsKeyPressed(Keys.Tab)) _selected = (_selected + 1) % _scene.Count;
        if (KeyboardState.IsKeyPressed(Keys.O)) _camera.IsPerspective = !_camera.IsPerspective;
        if (KeyboardState.IsKeyPressed(Keys.F)) _wireframe = !_wireframe;
        if (KeyboardState.IsKeyPressed(Keys.R)) ResetScene();

        if (KeyboardState.IsKeyPressed(Keys.Tab) || KeyboardState.IsKeyPressed(Keys.O) ||
            KeyboardState.IsKeyPressed(Keys.R))
        {
            _titleDirty = true;
        }

        // T3: Mutarea obiectului la poziția cursorului
        if (MouseState.IsButtonPressed(MouseButton.Left))
        {
            Vector2 worldPos = ScreenToWorld(MouseState.Position);
            sel.Transform.Position = worldPos;
        }

        // T4 (facultativ): Stabilire pivot (clic dreapta) și rotație în jurul lui (tasta P)
        if (MouseState.IsButtonPressed(MouseButton.Right))
        {
            _pivot = ScreenToWorld(MouseState.Position);
        }

        if (KeyboardState.IsKeyDown(Keys.P) && _pivot.HasValue)
        {
            float angleDeg = 90.0f * dt;
            float angleRad = MathHelper.DegreesToRadians(angleDeg);

            Vector2 offset = sel.Transform.Position - _pivot.Value;
            float newX = offset.X * MathF.Cos(angleRad) - offset.Y * MathF.Sin(angleRad);
            float newY = offset.X * MathF.Sin(angleRad) + offset.Y * MathF.Cos(angleRad);

            sel.Transform.Position = _pivot.Value + new Vector2(newX, newY);
            sel.Transform.Rotation += angleDeg; // își menține orientarea către pivot
        }

        // T5 (facultativ): Actualizare unghi orbită satelit (o rotație completă la 4 secunde -> 90 grade/sec)
        _satelliteOrbitAngle += 90.0f * dt;

        float camSpeed = 3.0f / _camera.Zoom;
        if (KeyboardState.IsKeyDown(Keys.A)) _camera.Position.X -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.D)) _camera.Position.X += camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.S)) _camera.Position.Y -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.W)) _camera.Position.Y += camSpeed * dt;

        _camera.Zoom *= MathF.Pow(1.1f, MouseState.ScrollDelta.Y);
        _camera.Zoom = Math.Clamp(_camera.Zoom, 0.1f, 10.0f);

        _secondTimer += args.Time;
        if (_secondTimer >= 1.0)
        {
            _lastFrames = _frames;
            _frames = 0;
            _secondTimer -= 1.0;
            _titleDirty = true;
        }

        if (_titleDirty)
        {
            string projection = _camera.IsPerspective ? "perspectivă" : "ortografică";
            Title = $"{_baseTitle} | selectat: {_scene[_selected].Name} | proiecție {projection} | FPS: {_lastFrames}";
            _titleDirty = false;
        }
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        _frames++;

        GL.ClearColor(0.12f, 0.14f, 0.18f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        SetWireframe(_wireframe);
        _shader.Use();

        _shader.SetMatrix4("uView", _camera.ViewMatrix);
        _shader.SetMatrix4("uProjection", _camera.ProjectionMatrix);

        foreach (SceneObject obj in _scene)
        {
            _shader.SetMatrix4("uModel", obj.Transform.ModelMatrix);
            _shader.SetColor("uTint", obj.Tint);
            obj.Mesh.Draw();
        }

        // T5 (facultativ): Calculul matricei și desenarea satelitului
        // Transformarea locală: scalare 0.3, translație 1.2 pe X, apoi rotația pe orbită
        Matrix4 satelliteTransform = Matrix4.CreateScale(0.3f, 0.3f, 1.0f) *
                                     Matrix4.CreateTranslation(1.2f, 0.0f, 0.0f) *
                                     Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(_satelliteOrbitAngle));

        // T5: Orbita și satelitul suferă aceleași modificări (se măresc/micșorează) deoarece matricea model 
        // a satelitului este înmulțită cu matricea model a obiectului selectat, preluând complet transformările acestuia.
        Matrix4 finalSatelliteModel = satelliteTransform * _scene[_selected].Transform.ModelMatrix;

        _shader.SetMatrix4("uModel", finalSatelliteModel);
        _shader.SetColor("uTint", _satellite.Tint);
        _satellite.Mesh.Draw();

        SwapBuffers();
    }

    protected override void OnUnload()
    {
        foreach (SceneObject obj in _scene)
        {
            obj.Dispose();
        }
        _satellite.Dispose();
        _shader.Dispose();
        base.OnUnload();
    }

    private Vector2 ScreenToWorld(Vector2 mousePixels)
    {
        // T3: Pasul 1 (pixel → NDC)
        // T3: Apare "1.0f -" la Y deoarece originea ecranului (mouse-ul) este în stânga-sus (axa Y crește în jos),
        // în timp ce în sistemul NDC originea este în centru, iar axa Y crește în sus.
        float ndcX = (mousePixels.X / ClientSize.X) * 2.0f - 1.0f;
        float ndcY = 1.0f - (mousePixels.Y / ClientSize.Y) * 2.0f;

        Vector4 ndcPosition = new Vector4(ndcX, ndcY, 0.0f, 1.0f);

        // T3: Pasul 2 (NDC → lume, cu inversa lui view * projection)
        Matrix4 inverseViewProj = Matrix4.Invert(_camera.ViewMatrix * _camera.ProjectionMatrix);
        Vector4 worldPosition = ndcPosition * inverseViewProj;

        // Normalizare perspectivă (împărțire la w) - util în special când proiecția este perspectivă
        if (worldPosition.W != 0.0f)
        {
            worldPosition.X /= worldPosition.W;
            worldPosition.Y /= worldPosition.W;
        }

        return new Vector2(worldPosition.X, worldPosition.Y);
    }

    private void ResetScene()
    {
        for (int i = 0; i < _scene.Count; i++)
        {
            _scene[i].Transform = _initialTransforms[i].Clone();
        }
        _pivot = null;
        _camera.Reset();
    }

    private static void SetWireframe(bool wireframe)
    {
        GL.PolygonMode(TriangleFace.FrontAndBack, wireframe ? PolygonMode.Line : PolygonMode.Fill);
    }
}