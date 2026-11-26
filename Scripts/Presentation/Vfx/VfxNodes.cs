namespace Fableland.Vfx;

using System;
using Godot;

/// <summary>
/// A pooled drawn mark: one canvas item whose <c>_Draw</c> is its style's
/// (<see cref="VfxStyle.Draw"/>). The director owns its life — it rents one, fills
/// <see cref="S"/>, advances its clock and asks for a redraw only when the medium's own frame
/// changes; a mark that follows a body is moved, not redrawn. Nothing is allocated while one
/// is alive (T40 §2–3).
/// </summary>
public partial class VfxMark : Node2D
{
    /// <summary>Marks drawn since boot / draws that threw — for the check.</summary>
    public static long Draws { get; private set; }
    public static int DrawErrors { get; private set; }
    /// <summary>Bytes drawing allocated while <see cref="MeasureAllocation"/> is on.</summary>
    public static long AllocatedInDraw { get; set; }
    public static bool MeasureAllocation;

    public VfxStyle Style;
    public VfxMarkState S;
    public VfxPriority Priority;
    public bool Live;
    public bool Looping;
    public bool Releasing;
    public VfxCanvas Canvas;
    public int LoopId;
    public Node2D Follow;
    public Vector2 FollowOffset;
    public float Age;            // seconds since it began (negative while delayed)
    public float OutAge;
    public int Step = int.MinValue;
    /// <summary>How many times this node has been rented — a recycled node is a new effect.</summary>
    public int Uses;
    /// <summary>The order pieces were rented in, across the whole director.</summary>
    public long Serial;

    // What a style reads while drawing.
    /// <summary>Life phase 0..1 (a loop: its place in the period, repeating), at the medium's step.</summary>
    public float T;
    /// <summary>Seconds alive, at the medium's step.</summary>
    public float Time;
    /// <summary>0..1 while a stopped loop leaves.</summary>
    public float Out;
    /// <summary>The hand's seed: constant for a smooth medium, new every step for one that boils.</summary>
    public uint Boil;

    public override void _Draw()
    {
        if (!Live || Style == null || Age < 0f) return;
        Draws++;
        long before = MeasureAllocation ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        try
        {
            VfxPen.Fade = Releasing ? 1f - Out : 1f;
            Style.Draw(this);
        }
        catch (Exception e)
        {
            VfxPen.End();
            if (DrawErrors++ < 3) GD.PushError($"[Vfx] {Style.Id} could not draw {S.Kind}: {e}");
        }
        finally
        {
            VfxPen.Fade = 1f;
            if (MeasureAllocation) AllocatedInDraw += GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    /// <summary>Everything a rented mark could carry over, cleared in one place (T40 §3).</summary>
    public void ResetForPool()
    {
        Live = false;
        Looping = false;
        Releasing = false;
        Canvas = VfxCanvas.Mid;
        LoopId = 0;
        Follow = null;
        FollowOffset = Vector2.Zero;
        Age = 0f;
        OutAge = 0f;
        Step = int.MinValue;
        T = 0f;
        Time = 0f;
        Out = 0f;
        Boil = 0u;
        Style = null;
        S = default;
        Priority = VfxPriority.Cosmetic;
        Visible = false;
        Position = Vector2.Zero;
        Rotation = 0f;
        Material = null;
        ZIndex = 0;
        TextureFilter = TextureFilterEnum.ParentNode;
    }

    /// <summary>True when the node holds nothing of a previous effect — what the check asserts.</summary>
    public bool IsClean => !Live && !Looping && !Releasing && LoopId == 0 && Follow == null && Style == null
        && S.Kind == VfxMarkKind.None && !Visible && Material == null && Age == 0f;
}

/// <summary>
/// A pooled one-shot emitter. Configured from a style's <see cref="VfxBurstRecipe"/> at the
/// moment it is rented — no process material, no GPU restart latency, and the pool is the
/// budget (R2 B9; Docs/VFX.gdd §7).
/// </summary>
public partial class VfxBurst : CpuParticles2D
{
    public bool Live;
    public VfxPriority Priority;
    public int LoopId;
    public float Age;
    public float Life;
    public int Particles;
    public int Uses;
    public long Serial;
    /// <summary>Each piece takes a colour between its two stops; mutated per rent, never re-made.</summary>
    public Gradient Tones;

    public void ResetForPool()
    {
        Live = false;
        LoopId = 0;
        Age = 0f;
        Life = 0f;
        Particles = 0;
        Priority = VfxPriority.Cosmetic;
        Emitting = false;
        Visible = false;
        Position = Vector2.Zero;
        Material = null;
        Texture = null;
        SpeedScale = 1.0;
        Preprocess = 0.0;
    }

    public bool IsClean => !Live && LoopId == 0 && Age == 0f && !Emitting && !Visible && Particles == 0;
}

/// <summary>One of an arena's ambient emitters: continuous, sparse, following the view.</summary>
public partial class VfxAmbient : CpuParticles2D
{
    public bool Live;
    public int Particles;
    public float BottomBand;
    public Gradient Tones;
}
