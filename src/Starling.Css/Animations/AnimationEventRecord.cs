using Starling.Dom;

namespace Starling.Css.Animations;

/// <summary>Kind of animation / transition DOM event a style engine wants
/// dispatched. The engines only record these facts; the embedder turns them
/// into real DOM events (Starling.Css must not depend on the bindings).</summary>
public enum AnimationEventKind : byte
{
    AnimationStart,
    AnimationIteration,
    AnimationEnd,
    TransitionRun,
    TransitionStart,
    TransitionEnd,
    TransitionCancel,
}

/// <summary>
/// One pending animation/transition DOM-event fact recorded by
/// <see cref="AnimationEngine"/> or <see cref="TransitionEngine"/> during a
/// tick or a cascade change. Events must never fire synchronously inside the
/// style pass (listeners mutate the DOM), so the engines queue these and the
/// embedder drains them at a safe point after the frame's style/layout work
/// (see <c>StarlingEngine.PrepareAnimationFrame</c>).
/// </summary>
public readonly struct AnimationEventRecord
{
    /// <summary>The event target.</summary>
    public readonly Element Element;
    public readonly AnimationEventKind Kind;
    /// <summary>The <c>animation-name</c> for animation events; the (kebab-case)
    /// transitioned property name for transition events.</summary>
    public readonly string Name;
    /// <summary>Spec <c>elapsedTime</c> in seconds — excludes the delay phase.</summary>
    public readonly double ElapsedSeconds;

    public AnimationEventRecord(Element element, AnimationEventKind kind, string name, double elapsedSeconds)
    {
        Element = element;
        Kind = kind;
        Name = name;
        ElapsedSeconds = elapsedSeconds;
    }
}
