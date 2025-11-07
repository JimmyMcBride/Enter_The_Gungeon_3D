using Godot;

namespace EnterTheGungeon3D.Game.Scenes.Modules.Player;

public partial class Player : CharacterBody3D
{
    private const float WalkSpeed = 10f;
    private const float DashSpeed = 30f;
    private const float DashDuration = 0.3f;

    // Smooth movement tuning
    private const float Acceleration = 40f; // units per second change toward target when moving
    private const float Deceleration = 35f; // units per second change toward target when stopping

    private MeshInstance3D _bodyMesh;
    private SpringArm3D _springArm; // reference to camera arm
    private uint _springArmSavedMask; // saved collision mask to restore after dash

    private bool _isDashing;
    private float _dashElapsed;
    private Vector3 _dashDirection;
    private Vector3 _lastMoveDirection = Vector3.Zero;
    private float _dashStartY; // lock Y during dash to avoid camera dip / ground clipping

    public override void _Ready()
    {
        _bodyMesh = GetNode<MeshInstance3D>("BodyMesh");
        _springArm = GetNodeOrNull<SpringArm3D>("SpringArm3D");
        if (_springArm != null)
            _springArmSavedMask = _springArm.CollisionMask;
    }

    public override void _Process(double delta)
    {
        FaceBodyMeshToMouse();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isDashing)
        {
            _dashElapsed += (float)delta;

            // Lock vertical position and perform manual dash motion to avoid MoveAndSlide ground snap jitter.
            var origin = GlobalTransform.Origin;
            origin.Y = _dashStartY; // keep Y stable
            GlobalPosition = origin;

            var motion = _dashDirection * DashSpeed * (float)delta;
            // Use MoveAndCollide for more predictable dash without floor snapping.
            var collision = MoveAndCollide(motion, testOnly: false, safeMargin: 0.001f);
            if (collision != null)
            {
                // Simple behavior: stop dash on collision (optional). Comment out if you want slide-through.
                EndDash();
            }

            if (_dashElapsed >= DashDuration)
                EndDash();
            return; // skip normal movement & gravity
        }

        // Capture input direction first so dash uses intended movement direction this frame.
        var inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var moveDir = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
        if (moveDir != Vector3.Zero)
            _lastMoveDirection = moveDir; // update last non-zero

        if (Input.IsActionJustPressed("dash"))
        {
            StartDash(moveDir);
            // After starting dash we return early in next frame; continue to skip regular movement modifications this frame.
        }

        var velocity = Velocity;

        if (!IsOnFloor())
            velocity += GetGravity() * (float)delta;

        // Smooth acceleration and deceleration on the horizontal plane.
        var desiredHorizontal = moveDir != Vector3.Zero ? moveDir * WalkSpeed : Vector3.Zero;
        var rate = moveDir != Vector3.Zero ? Acceleration : Deceleration;
        velocity.X = Mathf.MoveToward(velocity.X, desiredHorizontal.X, rate * (float)delta);
        velocity.Z = Mathf.MoveToward(velocity.Z, desiredHorizontal.Z, rate * (float)delta);

        Velocity = velocity;
        MoveAndSlide();
    }

    private void StartDash(Vector3 currentMoveDir)
    {
        _isDashing = true;
        _dashElapsed = 0f;
        _dashStartY = GlobalTransform.Origin.Y; // capture Y to keep stable during dash

        // Disable spring arm collision during dash to prevent camera from compressing into the ground.
        if (_springArm != null)
        {
            _springArmSavedMask = _springArm.CollisionMask;
            _springArm.CollisionMask = 0;
        }

        // Priority for dash direction:
        // 1. Current movement input
        // 2. Last non-zero movement direction
        // 3. Current horizontal velocity
        // 4. Facing forward of body mesh or player
        var dir = currentMoveDir;
        if (dir == Vector3.Zero)
            dir = _lastMoveDirection;
        if (dir == Vector3.Zero)
        {
            var horizontalVel = new Vector3(Velocity.X, 0, Velocity.Z);
            if (horizontalVel.Length() > 0.1f)
                dir = horizontalVel.Normalized();
        }
        if (dir == Vector3.Zero)
        {
            var forward = _bodyMesh != null ? -_bodyMesh.GlobalTransform.Basis.Z : -GlobalTransform.Basis.Z;
            dir = new Vector3(forward.X, 0, forward.Z).Normalized();
        }

        dir.Y = 0; // ensure horizontal
        _dashDirection = dir;

        // Zero vertical velocity to prevent gravity influence during dash.
        Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
    }

    private void EndDash()
    {
        _isDashing = false;
        Velocity = new Vector3(0, Velocity.Y, 0); // reset horizontal movement; vertical should remain stable

        // Restore spring arm collision settings.
        if (_springArm != null)
            _springArm.CollisionMask = _springArmSavedMask;
    }

    private void FaceBodyMeshToMouse()
    {
        if (_bodyMesh == null)
            return;

        var cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;

        var mousePos = GetViewport().GetMousePosition();
        var rayOrigin = cam.ProjectRayOrigin(mousePos);
        var rayDir = cam.ProjectRayNormal(mousePos);

        Vector3? targetPoint = null;

        var space = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayOrigin + rayDir * 2000f);
        var hit = space.IntersectRay(query);
        if (hit != null && hit.TryGetValue("position", out var posObj))
            targetPoint = (Vector3)posObj;

        if (targetPoint == null)
        {
            var plane = new Plane(Vector3.Up, _bodyMesh.GlobalTransform.Origin.Y);
            var planeHit = plane.IntersectsRay(rayOrigin, rayDir);
            if (planeHit.HasValue)
                targetPoint = planeHit.Value;
        }

        if (targetPoint.HasValue)
        {
            var lookAt = targetPoint.Value;
            lookAt.Y = _bodyMesh.GlobalTransform.Origin.Y;
            _bodyMesh.LookAt(lookAt, Vector3.Up);
        }
    }
}