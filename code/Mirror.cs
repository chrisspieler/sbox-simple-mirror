using System;
using Editor;
using Sandbox;

namespace SimpleMirror;

[Library( "func_mirror" )]
[HammerEntity]
[Title( "Mirror" ), Category( "Effects" ), Icon( "monitor" )]
[Solid, AutoApplyMaterial( "materials/mirror.vmat" )]
public partial class Mirror : ModelEntity
{

    [Property, Net, DefaultValue("materials/mirror.vmat")]
	public Material MirrorMaterial { get; set; } = Material.Load("materials/mirror.vmat");
	[Net] public float ClippingPlaneOffset { get; set; } = -1.0f;
	private static bool _debugDraw { get; set; } = false;
	private static bool _updateDraw { get; set; } = true;
	protected ScenePortal so;

	public override void Spawn()
	{
		Tags.Add("mirror");
		SetupPhysicsFromModel( PhysicsMotionType.Static );
		Transmit = TransmitType.Always;

		EnableAllCollisions = true;
		EnableSolidCollisions = true;
		EnableTouch = true;
		EnableDrawing = false;

		Predictable = false;
	}

	/// <summary>
	/// Useful in conjunction with <c>cl_debug_overlay_mirror</c> for 
	/// debugging the behavior of the mirror.
	/// </summary>
	[ConCmd.Client("cl_mirror_update_toggle")]
	public static void ToggleMirrorUpdate()
	{
		_updateDraw = !_updateDraw;
	}

	/// <summary>
	/// Displays various colored overlays relating to how the mirror functions.
	/// </summary>
	[ConCmd.Client("cl_debug_overlay_mirror")]
	public static void MirrorDebugDraw(bool enable )
	{
		_debugDraw = enable;
	}

	public override void ClientSpawn()
	{
		base.ClientSpawn();
		CreateScenePortalClient();
	}

	public override void OnNewModel( Model model )
	{
		if ( !Game.IsClient || so.IsValid() )
			return;
		base.OnNewModel( model );
		so = new ScenePortal( Game.SceneWorld, model, Transform, true, (int)Screen.Width )
		{
			Transform = this.Transform,
			Position = this.Position,
			Rotation = this.Rotation,
			RenderShadows = true,
			RenderingEnabled = true
		};
		SceneObject.RenderingEnabled = false;
	}

	protected void CreateScenePortalClient()
	{
		if ( so.IsValid() )
		{
			so.Delete();
		}

		DisableOriginal();
	}

	private async void DisableOriginal()
	{
		await GameTask.DelaySeconds( 0.1f );
		SceneObject.RenderingEnabled = false;
	}

	// For debug purposes, store the last frame's clipping plane.
	private Plane _clippingPlane;

	[Event.PreRender]
	public void UpdatePortalView()
	{
		if ( !Sandbox.Game.IsClient || !so.IsValid() )
			return;

        if (_debugDraw)
        {
			DebugOverlay.Text($"{Name}", Position);
			DebugOverlay.Sphere(Position, 2f, Color.Yellow);

			var clippingPlaneOrigin = so.ViewPosition + _clippingPlane.Normal * _clippingPlane.Distance;
			DebugOverlay.Sphere(clippingPlaneOrigin, 2.0f, Color.Green, 0, true);
			DebugOverlay.Text($"clipping plane ({Name})", clippingPlaneOrigin);
            // Draw clipping plane normal
            Vector3 lineEnd = clippingPlaneOrigin + _clippingPlane.Normal * 25f;
            DebugOverlay.Line(clippingPlaneOrigin, lineEnd, Color.Green, 0, true);

            var reflectDirection = so.ViewRotation.Forward;
			DebugOverlay.Text($"mirror camera {Name}", so.ViewPosition);
			DebugOverlay.Sphere(so.ViewPosition, 5f, Color.Red, 0, true);
			DebugOverlay.Line(so.ViewPosition, so.ViewPosition + reflectDirection * 25f, Color.Red, 0, true);
        }

        if (!_updateDraw)
            return;

        so.RenderingEnabled = true;
		so.Rotation = Rotation;
		so.Position = Position;

        Vector3 planeNormal = GetPlaneNormal();

        Plane p = new( Position, planeNormal );

		// Reflect
		Matrix viewMatrix = Matrix.CreateWorld( Camera.Position, Camera.Rotation.Forward, Camera.Rotation.Up );
		Matrix reflectMatrix = ReflectMatrix( viewMatrix, p );

		// Apply Rotation
		Vector3 reflectionPosition = reflectMatrix.Transform( Camera.Position );
		Rotation reflectionRotation = ReflectRotation( Camera.Rotation, planeNormal );

		so.ViewPosition = reflectionPosition;
		so.ViewRotation = reflectionRotation;

		so.Aspect = Screen.Width / Screen.Height;

		so.FieldOfView = Camera.FieldOfView;

		_clippingPlane = new Plane(Position - so.ViewPosition, planeNormal);
		_clippingPlane.Distance += ClippingPlaneOffset;

		so.SetClipPlane(_clippingPlane);
	}

	public Vector3 GetPlaneNormal()
	{
		TraceResult tr = Trace.Ray( Camera.Position, Position )
			.WithTag( "mirror" )
			.EntitiesOnly()
			.IncludeClientside()
			.Run();
		return tr.Normal;
	}

	private Rotation ReflectRotation( Rotation source, Vector3 normal )
	{
		return Rotation.LookAt( Vector3.Reflect( source * Vector3.Forward, normal ), Vector3.Reflect( source * Vector3.Up, normal ) );
	}

	public Matrix ReflectMatrix( Matrix m, Plane plane )
	{
		m.Numerics.M11 = (1.0f - 2.0f * plane.Normal.x * plane.Normal.x);
		m.Numerics.M21 = (-2.0f * plane.Normal.x * plane.Normal.y);
		m.Numerics.M31 = (-2.0f * plane.Normal.x * plane.Normal.z);
		m.Numerics.M41 = (-2.0f * -plane.Distance * plane.Normal.x);

		m.Numerics.M12 = (-2.0f * plane.Normal.y * plane.Normal.x);
		m.Numerics.M22 = (1.0f - 2.0f * plane.Normal.y * plane.Normal.y);
		m.Numerics.M32 = (-2.0f * plane.Normal.y * plane.Normal.z);
		m.Numerics.M42 = (-2.0f * -plane.Distance * plane.Normal.y);

		m.Numerics.M13 = (-2.0f * plane.Normal.z * plane.Normal.x);
		m.Numerics.M23 = (-2.0f * plane.Normal.z * plane.Normal.y);
		m.Numerics.M33 = (1.0f - 2.0f * plane.Normal.z * plane.Normal.z);
		m.Numerics.M43 = (-2.0f * -plane.Distance * plane.Normal.z);

		m.Numerics.M14 = 0.0f;
		m.Numerics.M24 = 0.0f;
		m.Numerics.M34 = 0.0f;
		m.Numerics.M44 = 1.0f;

		return m;
	}
}
