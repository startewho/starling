namespace Starling.Dom.Events;

/// <summary>AnimationEvent — CSS Animations 1 §5. Carried by
/// <c>animationstart</c> / <c>animationiteration</c> / <c>animationend</c>.
/// <see cref="ElapsedTime"/> is in seconds and excludes the delay phase.</summary>
public class AnimationEvent : Event
{
    public AnimationEvent(string type, EventInit init = default) : base(type, init) { }
    public string AnimationName { get; init; } = string.Empty;
    public double ElapsedTime { get; init; }
    public string PseudoElement { get; init; } = string.Empty;
}

/// <summary>TransitionEvent — CSS Transitions 1 §4 (plus <c>transitionrun</c> /
/// <c>transitioncancel</c> from Transitions 2). <see cref="ElapsedTime"/> is in
/// seconds and excludes the delay phase.</summary>
public class TransitionEvent : Event
{
    public TransitionEvent(string type, EventInit init = default) : base(type, init) { }
    public string PropertyName { get; init; } = string.Empty;
    public double ElapsedTime { get; init; }
    public string PseudoElement { get; init; } = string.Empty;
}
