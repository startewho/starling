using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// §27.2 The Promise intrinsic. Owns the Promise constructor, the
/// reaction/resolution machinery (§27.2.1.*), prototype methods
/// (<c>then</c>/<c>catch</c>/<c>finally</c>, §27.2.5.*) and the
/// constructor-level statics (<c>resolve</c>/<c>reject</c>/<c>all</c>/
/// <c>allSettled</c>/<c>any</c>/<c>race</c>/<c>withResolvers</c>/
/// <c>allKeyed</c>/<c>allSettledKeyed</c>, §27.2.4.*).
/// </summary>
/// <remarks>
/// Reactions are scheduled onto <see cref="JsRealm.Microtasks"/>; the VM
/// drains the queue at the bottom of every top-level <c>Run</c>, so unit
/// tests see settlement without manual pumping. When a host installs a
/// scheduler via <see cref="JsRuntime.SetMicrotaskScheduler"/>, jobs are
/// handed off to the host loop instead.
/// </remarks>
public static class PromiseCtor
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.PromisePrototype;

        // ----------------------------------------------------------- Constructor
        // §27.2.3.1 Promise(executor) — executor MUST be callable. The
        // executor runs synchronously, receiving the (resolve, reject)
        // pair built from this promise's capability. Any thrown value
        // becomes the rejection reason.
        var ctor = new JsNativeFunction("Promise", (newTarget, args) =>
        {
            // §27.2.3.1 step 1: Promise requires `new` (or a derived super()).
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Promise requires 'new'"));
            }

            if (args.Length == 0 || !AbstractOperations.IsCallable(args[0]))
            {
                throw new JsThrow(realm.NewTypeError("Promise resolver is not a function"));
            }

            var executor = args[0];

            // §27.2.3.1 step 3: OrdinaryCreateFromConstructor — prototype from
            // new.target so `class P extends Promise {}` produces a P-prototyped
            // promise carrying the [[PromiseState]] slots.
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto,
                static r => r.PromisePrototype);
            var promise = new JsPromise(instProto);
            var (resolve, reject) = CreateResolvingFunctions(realm, promise);
            try
            {
                AbstractOperations.Call(realm.ActiveVm, executor, JsValue.Undefined,
                    new[] { resolve, reject });
            }
            catch (JsThrow ex)
            {
                // §27.2.3.1 step 10 — the abrupt executor completion goes
                // through the REJECT RESOLVING FUNCTION, not [[Reject]]
                // directly: if the executor already called resolve/reject the
                // shared [[AlreadyResolved]] flag makes this a no-op.
                AbstractOperations.Call(realm.ActiveVm, reject, JsValue.Undefined, new[] { ex.Value });
            }
            return JsValue.Object(promise);
        }, isConstructor: true);
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        ctor.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(1), writable: false, enumerable: false, configurable: true));
        ctor.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String("Promise"), writable: false, enumerable: false, configurable: true));

        var speciesGetter = new JsNativeFunction(realm, "get [Symbol.species]", 0,
            (thisV, _) => thisV, isConstructor: false);
        ctor.DefineOwnProperty(SymbolCtor.Species,
            PropertyDescriptor.Accessor(speciesGetter, null, enumerable: false, configurable: true));

        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Promise"), writable: false, enumerable: false, configurable: true));

        // --------------------------------------------------------- Statics
        // §27.2.4 step 1 (every static): the receiver C must be an Object —
        // `Promise.all.call(undefined, …)` is a TypeError, thrown BEFORE the
        // iterable is touched.
        void RequireObjectReceiver(JsValue thisV, string name)
        {
            if (!thisV.IsObject)
            {
                throw new JsThrow(realm.NewTypeError($"Promise.{name} called on non-object"));
            }
        }
        IntrinsicHelpers.DefineMethod(realm, ctor, "all", 1, (thisV, args) =>
        {
            RequireObjectReceiver(thisV, "all");
            return Combinator(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined, CombinatorKind.All);
        });
        IntrinsicHelpers.DefineMethod(realm, ctor, "allKeyed", 1, (thisV, args) =>
        {
            RequireObjectReceiver(thisV, "allKeyed");
            return KeyedCombinator(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined, settled: false);
        });
        IntrinsicHelpers.DefineMethod(realm, ctor, "allSettled", 1, (thisV, args) =>
        {
            RequireObjectReceiver(thisV, "allSettled");
            return Combinator(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined, CombinatorKind.AllSettled);
        });
        IntrinsicHelpers.DefineMethod(realm, ctor, "allSettledKeyed", 1, (thisV, args) =>
        {
            RequireObjectReceiver(thisV, "allSettledKeyed");
            return KeyedCombinator(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined, settled: true);
        });
        IntrinsicHelpers.DefineMethod(realm, ctor, "any", 1, (thisV, args) =>
        {
            RequireObjectReceiver(thisV, "any");
            return Combinator(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined, CombinatorKind.Any);
        });
        IntrinsicHelpers.DefineMethod(realm, ctor, "race", 1, (thisV, args) =>
        {
            RequireObjectReceiver(thisV, "race");
            return Combinator(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined, CombinatorKind.Race);
        });
        IntrinsicHelpers.DefineMethod(realm, ctor, "reject", 1, (thisV, args) =>
        {
            RequireObjectReceiver(thisV, "reject");
            var capability = NewPromiseCapability(realm, thisV);
            AbstractOperations.Call(realm.ActiveVm, capability.Reject, JsValue.Undefined,
                new[] { args.Length > 0 ? args[0] : JsValue.Undefined });
            return JsValue.Object(capability.Promise);
        });
        IntrinsicHelpers.DefineMethod(realm, ctor, "resolve", 1, (thisV, args) =>
        {
            RequireObjectReceiver(thisV, "resolve");
            return PromiseResolve(realm, thisV, args.Length > 0 ? args[0] : JsValue.Undefined);
        });
        IntrinsicHelpers.DefineMethod(realm, ctor, "withResolvers", 0, (thisV, args) =>
        {
            RequireObjectReceiver(thisV, "withResolvers");
            var capability = NewPromiseCapability(realm, thisV);
            var obj = realm.NewOrdinaryObject();
            obj.DefineOwnProperty("promise",
                PropertyDescriptor.Data(JsValue.Object(capability.Promise), writable: true, enumerable: true, configurable: true));
            obj.DefineOwnProperty("resolve",
                PropertyDescriptor.Data(capability.Resolve, writable: true, enumerable: true, configurable: true));
            obj.DefineOwnProperty("reject",
                PropertyDescriptor.Data(capability.Reject, writable: true, enumerable: true, configurable: true));
            return JsValue.Object(obj);
        });

        // --------------------------------------------------------- Prototype
        IntrinsicHelpers.DefineMethod(realm, proto, "then", 2, (thisV, args) =>
        {
            var onFulfilled = args.Length > 0 ? args[0] : JsValue.Undefined;
            var onRejected = args.Length > 1 ? args[1] : JsValue.Undefined;
            return Then(realm, thisV, onFulfilled, onRejected);
        });

        IntrinsicHelpers.DefineMethod(realm, proto, "catch", 1, (thisV, args) =>
        {
            // §27.2.5.1 — return ? Invoke(this, "then", « undefined, onRejected »).
            var onRejected = args.Length > 0 ? args[0] : JsValue.Undefined;
            return Invoke(realm, thisV, "then", new[] { JsValue.Undefined, onRejected });
        });

        IntrinsicHelpers.DefineMethod(realm, proto, "finally", 1, (thisV, args) =>
        {
            var onFinally = args.Length > 0 ? args[0] : JsValue.Undefined;
            return Finally(realm, thisV, onFinally);
        });

        realm.PromiseConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("Promise",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    // ====================================================================
    //                       Resolving-function machinery
    // ====================================================================

    /// <summary>§27.2.1.3 CreateResolvingFunctions. Produces the
    /// (resolve, reject) pair handed to the executor. Both share a
    /// single <c>[[AlreadyResolved]]</c> flag — whichever runs first
    /// settles the promise; the other becomes a no-op.</summary>
    private static (JsValue Resolve, JsValue Reject) CreateResolvingFunctions(
        JsRealm realm, JsPromise promise)
    {
        var alreadyResolved = new bool[1]; // boxed flag shared by closures

        var resolve = new JsNativeFunction(realm, "", 1, (thisV, args) =>
        {
            if (alreadyResolved[0])
            {
                return JsValue.Undefined;
            }

            alreadyResolved[0] = true;
            var value = args.Length > 0 ? args[0] : JsValue.Undefined;
            ResolvePromise(realm, promise, value);
            return JsValue.Undefined;
        }, isConstructor: false);

        var reject = new JsNativeFunction(realm, "", 1, (thisV, args) =>
        {
            if (alreadyResolved[0])
            {
                return JsValue.Undefined;
            }

            alreadyResolved[0] = true;
            var reason = args.Length > 0 ? args[0] : JsValue.Undefined;
            RejectPromise(realm, promise, reason);
            return JsValue.Undefined;
        }, isConstructor: false);

        return (JsValue.Object(resolve), JsValue.Object(reject));
    }

    /// <summary>B1b-2c — public surface for async-function machinery.
    /// Settles <paramref name="promise"/> via the standard adoption path.</summary>
    public static void Resolve(JsRealm realm, JsPromise promise, JsValue resolution)
        => ResolvePromise(realm, promise, resolution);

    /// <summary>B1b-2c — public surface mirror of <see cref="Resolve"/>.</summary>
    public static void Reject(JsRealm realm, JsPromise promise, JsValue reason)
        => RejectPromise(realm, promise, reason);

    /// <summary>§27.2.1.4 Promise Resolve Functions. Adoption rules:
    /// resolving with the promise itself is a TypeError; resolving with a
    /// thenable schedules a microtask to follow it; otherwise fulfill
    /// directly.</summary>
    private static void ResolvePromise(JsRealm realm, JsPromise promise, JsValue resolution)
    {
        if (resolution.IsObject && ReferenceEquals(resolution.AsObject, promise))
        {
            RejectPromise(realm, promise, realm.NewTypeError("Chaining cycle detected for promise"));
            return;
        }

        if (!resolution.IsObject)
        {
            FulfillPromise(realm, promise, resolution);
            return;
        }

        // Thenable adoption.
        JsValue then;
        try
        {
            then = AbstractOperations.Get(realm.ActiveVm, resolution.AsObject, "then");
        }
        catch (JsThrow ex)
        {
            RejectPromise(realm, promise, ex.Value);
            return;
        }
        if (!AbstractOperations.IsCallable(then))
        {
            FulfillPromise(realm, promise, resolution);
            return;
        }

        // §27.2.2.1 NewPromiseResolveThenableJob — schedule the .then call
        // for the next microtask. Spec uses a job record; we capture
        // directly.
        realm.Microtasks.Enqueue(() =>
        {
            var (innerResolve, innerReject) = CreateResolvingFunctions(realm, promise);
            try
            {
                AbstractOperations.Call(realm.ActiveVm, then, resolution,
                    new[] { innerResolve, innerReject });
            }
            catch (JsThrow ex)
            {
                AbstractOperations.Call(realm.ActiveVm, innerReject, JsValue.Undefined,
                    new[] { ex.Value });
            }
        });
    }

    /// <summary>§27.2.1.5 FulfillPromise — transition state and flush
    /// fulfillment reactions to the microtask queue.</summary>
    private static void FulfillPromise(JsRealm realm, JsPromise promise, JsValue value)
    {
        if (!promise.Fulfill(value))
        {
            return;
        }

        var reactions = promise.FulfillReactions.ToArray();
        promise.FulfillReactions.Clear();
        promise.RejectReactions.Clear();
        foreach (var reaction in reactions)
        {
            EnqueueReactionJob(realm, reaction, value);
        }
    }

    /// <summary>§27.2.1.7 RejectPromise — symmetric to fulfill. Step 7:
    /// when the promise is not handled, HostPromiseRejectionTracker is told
    /// so the host can report the rejection if no handler arrives.</summary>
    private static void RejectPromise(JsRealm realm, JsPromise promise, JsValue reason)
    {
        if (!promise.Reject(reason))
        {
            return;
        }

        var reactions = promise.RejectReactions.ToArray();
        promise.FulfillReactions.Clear();
        promise.RejectReactions.Clear();
        if (!promise.IsHandled)
        {
            realm.OnUnhandledRejection?.Invoke(promise);
        }

        foreach (var reaction in reactions)
        {
            EnqueueReactionJob(realm, reaction, reason);
        }
    }

    /// <summary>§27.2.2.1 NewPromiseReactionJob — schedules a single
    /// reaction onto the microtask queue. The job invokes the handler
    /// (or applies pass-through identity/thrower when omitted) and
    /// settles the chained promise with the outcome.</summary>
    private static void EnqueueReactionJob(JsRealm realm, PromiseReaction reaction, JsValue argument)
    {
        realm.Microtasks.Enqueue(() =>
        {
            var cap = reaction.Capability;
            var handler = reaction.Handler;
            JsValue handlerResult;
            bool isThrow;

            if (!AbstractOperations.IsCallable(handler))
            {
                // Pass-through: fulfill-reaction with no handler → identity;
                // reject-reaction with no handler → re-throw.
                handlerResult = argument;
                isThrow = reaction.Type == PromiseReactionType.Reject;
            }
            else
            {
                try
                {
                    handlerResult = AbstractOperations.Call(realm.ActiveVm, handler,
                        JsValue.Undefined, new[] { argument });
                    isThrow = false;
                }
                catch (JsThrow ex)
                {
                    handlerResult = ex.Value;
                    isThrow = true;
                }
            }

            if (isThrow)
            {
                AbstractOperations.Call(realm.ActiveVm, cap.Reject, JsValue.Undefined, new[] { handlerResult });
            }
            else
            {
                AbstractOperations.Call(realm.ActiveVm, cap.Resolve, JsValue.Undefined, new[] { handlerResult });
            }
        });
    }

    // ====================================================================
    //                        .then / .catch / .finally
    // ====================================================================

    /// <summary>§7.3.21 Invoke(V, P, args) — observable method call; a
    /// primitive receiver walks its wrapper prototype.</summary>
    private static JsValue Invoke(JsRealm realm, JsValue target, string name, JsValue[] args)
    {
        var vm = realm.ActiveVm;
        JsValue method;
        if (target.IsObject)
        {
            method = AbstractOperations.Get(vm, target.AsObject, name);
        }
        else if (target.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError($"Cannot read '{name}' of {(target.IsNull ? "null" : "undefined")}"));
        }
        else
        {
            method = AbstractOperations.Get(vm, AbstractOperations.ToObject(realm, target), name);
        }

        if (!AbstractOperations.IsCallable(method))
        {
            throw new JsThrow(realm.NewTypeError($"'{name}' is not a function"));
        }

        return AbstractOperations.Call(vm, method, target, args);
    }

    /// <summary>§7.3.24 SpeciesConstructor(O, defaultConstructor).</summary>
    private static JsValue SpeciesConstructor(JsRealm realm, JsObject obj, JsObject defaultCtor)
    {
        var vm = realm.ActiveVm;
        var c = AbstractOperations.Get(vm, obj, "constructor");
        if (c.IsUndefined)
        {
            return JsValue.Object(defaultCtor);
        }

        if (!c.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("constructor value is not an object"));
        }

        var species = AbstractOperations.Get(vm, c.AsObject, JsPropertyKey.Symbol(SymbolCtor.Species));
        if (species.IsNullish)
        {
            return JsValue.Object(defaultCtor);
        }

        if (AbstractOperations.IsConstructor(species))
        {
            return species;
        }

        throw new JsThrow(realm.NewTypeError("@@species is not a constructor"));
    }

    private static JsValue Then(JsRealm realm, JsValue thisV, JsValue onFulfilled, JsValue onRejected)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsPromise self)
        {
            throw new JsThrow(realm.NewTypeError("Promise.prototype.then called on non-Promise"));
        }

        // §27.2.5.4 step 3–4: the chained promise comes from the species
        // constructor, so subclass `then` yields subclass instances.
        var speciesC = SpeciesConstructor(realm, self, realm.PromiseConstructor!);
        var capability = NewPromiseCapability(realm, speciesC);
        return PerformPromiseThen(realm, self, onFulfilled, onRejected, capability);
    }

    /// <summary>§27.2.5.4.1 PerformPromiseThen.</summary>
    private static JsValue PerformPromiseThen(JsRealm realm, JsPromise self,
        JsValue onFulfilled, JsValue onRejected, PromiseCapability capability)
    {
        var fulfillHandler = AbstractOperations.IsCallable(onFulfilled) ? onFulfilled : JsValue.Undefined;
        var rejectHandler = AbstractOperations.IsCallable(onRejected) ? onRejected : JsValue.Undefined;

        var fulfillReaction = new PromiseReaction(capability, PromiseReactionType.Fulfill, fulfillHandler);
        var rejectReaction = new PromiseReaction(capability, PromiseReactionType.Reject, rejectHandler);

        switch (self.State)
        {
            case PromiseState.Pending:
                self.FulfillReactions.Add(fulfillReaction);
                self.RejectReactions.Add(rejectReaction);
                break;
            case PromiseState.Fulfilled:
                EnqueueReactionJob(realm, fulfillReaction, self.Result);
                break;
            case PromiseState.Rejected:
                // HostPromiseRejectionTracker(promise, "handle") — a late
                // handler retracts a queued unhandled-rejection report.
                if (!self.IsHandled)
                {
                    realm.OnRejectionHandled?.Invoke(self);
                }

                EnqueueReactionJob(realm, rejectReaction, self.Result);
                break;
        }

        // §27.2.5.4.1 step 12 — the promise now has a handler regardless of
        // which callbacks were supplied.
        self.IsHandled = true;

        return JsValue.Object(capability.Promise);
    }

    /// <summary>§27.2.5.3 Promise.prototype.finally — generic over any
    /// object receiver with a callable <c>then</c>; wraps the user handler so
    /// it runs on both branches, then forwards the original outcome via a
    /// species-resolved inner promise.</summary>
    private static JsValue Finally(JsRealm realm, JsValue thisV, JsValue onFinally)
    {
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Promise.prototype.finally called on non-object"));
        }

        var speciesC = SpeciesConstructor(realm, thisV.AsObject, realm.PromiseConstructor!);

        JsValue thenFinally;
        JsValue catchFinally;
        if (!AbstractOperations.IsCallable(onFinally))
        {
            thenFinally = onFinally;
            catchFinally = onFinally;
        }
        else
        {
            thenFinally = JsValue.Object(new JsNativeFunction(realm, "", 1, (selfThis, args) =>
            {
                var value = args.Length > 0 ? args[0] : JsValue.Undefined;
                var result = AbstractOperations.Call(realm.ActiveVm, onFinally, JsValue.Undefined, Array.Empty<JsValue>());
                var followed = PromiseResolve(realm, speciesC, result);
                var valueThunk = new JsNativeFunction(realm, "", 0, (_, _) => value, isConstructor: false);
                return Invoke(realm, followed, "then", new[] { JsValue.Object(valueThunk) });
            }, isConstructor: false));

            catchFinally = JsValue.Object(new JsNativeFunction(realm, "", 1, (selfThis, args) =>
            {
                var reason = args.Length > 0 ? args[0] : JsValue.Undefined;
                var result = AbstractOperations.Call(realm.ActiveVm, onFinally, JsValue.Undefined, Array.Empty<JsValue>());
                var followed = PromiseResolve(realm, speciesC, result);
                var thrower = new JsNativeFunction(realm, "", 0, (JsValue _, JsValue[] _) => throw new JsThrow(reason), isConstructor: false);
                return Invoke(realm, followed, "then", new[] { JsValue.Object(thrower) });
            }, isConstructor: false));
        }

        return Invoke(realm, thisV, "then", new[] { thenFinally, catchFinally });
    }

    // ====================================================================
    //                              Statics
    // ====================================================================

    /// <summary>§27.2.4.7.1 PromiseResolve(C, x) — returns x verbatim when it
    /// is a promise whose observable <c>constructor</c> is C; otherwise
    /// builds a C-capability promise and resolves it with x.</summary>
    private static JsValue PromiseResolve(JsRealm realm, JsValue c, JsValue x)
    {
        if (x.IsObject && x.AsObject is JsPromise)
        {
            var xConstructor = AbstractOperations.Get(realm.ActiveVm, x.AsObject, "constructor");
            if (AbstractOperations.SameValue(xConstructor, c))
            {
                return x;
            }
        }

        var capability = NewPromiseCapability(realm, c);
        AbstractOperations.Call(realm.ActiveVm, capability.Resolve, JsValue.Undefined, new[] { x });
        return JsValue.Object(capability.Promise);
    }

    private enum CombinatorKind : byte
    {
        All = 0,
        AllSettled = 1,
        Any = 2,
        Race = 3,
    }

    /// <summary>Shared driver for §27.2.4.{1,2,3,5} — NewPromiseCapability(C),
    /// GetPromiseResolve, GetIterator, then the per-kind element loop. Abrupt
    /// completions after the capability exists close the iterator (when not
    /// exhausted) and reject the capability (IfAbruptRejectPromise).</summary>
    private static JsValue Combinator(JsRealm realm, JsValue thisV, JsValue iterable, CombinatorKind kind)
    {
        var vm = realm.ActiveVm;
        var capability = NewPromiseCapability(realm, thisV);
        var record = default(IteratorRecord);
        var haveIterator = false;
        try
        {
            var promiseResolve = GetPromiseResolve(realm, thisV);
            record = AbstractOperations.GetIterator(realm, vm, iterable);
            haveIterator = true;
            return PerformCombinator(realm, thisV, capability, promiseResolve, ref record, kind);
        }
        catch (JsThrow ex)
        {
            if (haveIterator && !record.Done)
            {
                AbstractOperations.IteratorClose(vm, record, isThrowing: true);
            }

            AbstractOperations.Call(vm, capability.Reject, JsValue.Undefined, new[] { ex.Value });
            return JsValue.Object(capability.Promise);
        }
    }

    private static JsValue PerformCombinator(JsRealm realm, JsValue c, PromiseCapability capability,
        JsValue promiseResolve, ref IteratorRecord record, CombinatorKind kind)
    {
        var vm = realm.ActiveVm;
        var values = new List<JsValue>();
        var remaining = new int[1] { 1 };
        var index = 0;
        while (true)
        {
            var step = AbstractOperations.IteratorStep(realm, vm, ref record);
            if (step is null)
            {
                if (kind != CombinatorKind.Race && --remaining[0] == 0)
                {
                    FinishCombinator(realm, capability, values, kind);
                }

                return JsValue.Object(capability.Promise);
            }

            JsValue nextValue;
            try
            {
                nextValue = AbstractOperations.IteratorValue(vm, step.Value);
            }
            catch
            {
                record = record with { Done = true };
                throw;
            }

            var nextPromise = AbstractOperations.Call(vm, promiseResolve, c, new[] { nextValue });
            JsValue onFulfilled;
            JsValue onRejected;
            switch (kind)
            {
                case CombinatorKind.All:
                    values.Add(JsValue.Undefined);
                    onFulfilled = MakeElementFunction(realm, values, index, remaining, capability, ElementKind.AllFulfill, new bool[1]);
                    onRejected = capability.Reject;
                    break;
                case CombinatorKind.AllSettled:
                {
                    values.Add(JsValue.Undefined);
                    var alreadyCalled = new bool[1];
                    onFulfilled = MakeElementFunction(realm, values, index, remaining, capability, ElementKind.SettledFulfill, alreadyCalled);
                    onRejected = MakeElementFunction(realm, values, index, remaining, capability, ElementKind.SettledReject, alreadyCalled);
                    break;
                }
                case CombinatorKind.Any:
                    values.Add(JsValue.Undefined);
                    onFulfilled = capability.Resolve;
                    onRejected = MakeElementFunction(realm, values, index, remaining, capability, ElementKind.AnyReject, new bool[1]);
                    break;
                default:
                    onFulfilled = capability.Resolve;
                    onRejected = capability.Reject;
                    break;
            }
            if (kind != CombinatorKind.Race)
            {
                remaining[0]++;
            }

            Invoke(realm, nextPromise, "then", new[] { onFulfilled, onRejected });
            index++;
        }
    }

    private enum ElementKind : byte
    {
        AllFulfill = 0,
        SettledFulfill = 1,
        SettledReject = 2,
        AnyReject = 3,
    }

    /// <summary>The resolve/reject-element functions of §27.2.4.1.3,
    /// §27.2.4.2.2/.3 and §27.2.4.3.2 — non-constructor, name "", length 1,
    /// one-shot via [[AlreadyCalled]] (shared per element for allSettled).</summary>
    private static JsValue MakeElementFunction(JsRealm realm, List<JsValue> values, int index,
        int[] remaining, PromiseCapability capability, ElementKind kind, bool[] alreadyCalled)
    {
        var fn = new JsNativeFunction(realm, "", 1, (_, args) =>
        {
            if (alreadyCalled[0])
            {
                return JsValue.Undefined;
            }

            alreadyCalled[0] = true;
            var arg = args.Length > 0 ? args[0] : JsValue.Undefined;
            values[index] = kind switch
            {
                ElementKind.AllFulfill or ElementKind.AnyReject => arg,
                ElementKind.SettledFulfill => MakeSettledEntry(realm, "fulfilled", "value", arg),
                _ => MakeSettledEntry(realm, "rejected", "reason", arg),
            };
            if (--remaining[0] == 0)
            {
                var settleKind = kind == ElementKind.AnyReject ? CombinatorKind.Any : CombinatorKind.All;
                FinishCombinator(realm, capability, values, settleKind);
            }

            return JsValue.Undefined;
        }, isConstructor: false);
        return JsValue.Object(fn);
    }

    private static JsValue MakeSettledEntry(JsRealm realm, string status, string slot, JsValue value)
    {
        var entry = realm.NewOrdinaryObject();
        entry.DefineOwnProperty("status",
            PropertyDescriptor.Data(JsValue.String(status), writable: true, enumerable: true, configurable: true));
        entry.DefineOwnProperty(slot,
            PropertyDescriptor.Data(value, writable: true, enumerable: true, configurable: true));
        return JsValue.Object(entry);
    }

    private static void FinishCombinator(JsRealm realm, PromiseCapability capability,
        List<JsValue> values, CombinatorKind kind)
    {
        if (kind == CombinatorKind.Any)
        {
            var error = MakeAggregateError(realm, values, "All promises were rejected");
            AbstractOperations.Call(realm.ActiveVm, capability.Reject, JsValue.Undefined, new[] { error });
            return;
        }

        var arr = new JsArray(realm, values);
        AbstractOperations.Call(realm.ActiveVm, capability.Resolve, JsValue.Undefined,
            new[] { JsValue.Object(arr) });
    }

    /// <summary>Promise.allKeyed / Promise.allSettledKeyed (await-dictionary
    /// proposal): combine the values of an object's own enumerable properties;
    /// the result is a null-prototype object keyed like the input.</summary>
    private static JsValue KeyedCombinator(JsRealm realm, JsValue thisV, JsValue promises, bool settled)
    {
        var vm = realm.ActiveVm;
        var capability = NewPromiseCapability(realm, thisV);
        try
        {
            var promiseResolve = GetPromiseResolve(realm, thisV);
            if (!promises.IsObject)
            {
                throw new JsThrow(realm.NewTypeError(
                    $"Promise.{(settled ? "allSettledKeyed" : "allKeyed")} argument must be an object"));
            }

            var source = promises.AsObject;
            var allKeys = new List<JsPropertyKey>(source.OwnPropertyKeys);
            var keys = new List<JsPropertyKey>();
            var values = new List<JsValue>();
            var remaining = new int[1] { 1 };
            var index = 0;
            foreach (var key in allKeys)
            {
                var desc = key.IsSymbol
                    ? source.GetOwnPropertyDescriptor(key.AsSymbol)
                    : source.GetOwnPropertyDescriptor(key.AsString);
                if (desc is not { } dv || !dv.Enumerable)
                {
                    continue;
                }

                keys.Add(key);
                values.Add(JsValue.Undefined);
                var nextValue = AbstractOperations.Get(vm, source, key);
                var nextPromise = AbstractOperations.Call(vm, promiseResolve, thisV, new[] { nextValue });
                JsValue onFulfilled;
                JsValue onRejected;
                if (settled)
                {
                    var alreadyCalled = new bool[1];
                    onFulfilled = MakeKeyedElementFunction(realm, keys, values, index, remaining, capability, ElementKind.SettledFulfill, alreadyCalled);
                    onRejected = MakeKeyedElementFunction(realm, keys, values, index, remaining, capability, ElementKind.SettledReject, alreadyCalled);
                }
                else
                {
                    onFulfilled = MakeKeyedElementFunction(realm, keys, values, index, remaining, capability, ElementKind.AllFulfill, new bool[1]);
                    onRejected = capability.Reject;
                }
                remaining[0]++;
                Invoke(realm, nextPromise, "then", new[] { onFulfilled, onRejected });
                index++;
            }
            if (--remaining[0] == 0)
            {
                var result = MakeKeyedResult(keys, values);
                AbstractOperations.Call(vm, capability.Resolve, JsValue.Undefined, new[] { result });
            }

            return JsValue.Object(capability.Promise);
        }
        catch (JsThrow ex)
        {
            AbstractOperations.Call(vm, capability.Reject, JsValue.Undefined, new[] { ex.Value });
            return JsValue.Object(capability.Promise);
        }
    }

    private static JsValue MakeKeyedElementFunction(JsRealm realm, List<JsPropertyKey> keys,
        List<JsValue> values, int index, int[] remaining, PromiseCapability capability,
        ElementKind kind, bool[] alreadyCalled)
    {
        var fn = new JsNativeFunction(realm, "", 1, (_, args) =>
        {
            if (alreadyCalled[0])
            {
                return JsValue.Undefined;
            }

            alreadyCalled[0] = true;
            var arg = args.Length > 0 ? args[0] : JsValue.Undefined;
            values[index] = kind switch
            {
                ElementKind.AllFulfill => arg,
                ElementKind.SettledFulfill => MakeSettledEntry(realm, "fulfilled", "value", arg),
                _ => MakeSettledEntry(realm, "rejected", "reason", arg),
            };
            if (--remaining[0] == 0)
            {
                var result = MakeKeyedResult(keys, values);
                AbstractOperations.Call(realm.ActiveVm, capability.Resolve, JsValue.Undefined, new[] { result });
            }

            return JsValue.Undefined;
        }, isConstructor: false);
        return JsValue.Object(fn);
    }

    private static JsValue MakeKeyedResult(List<JsPropertyKey> keys, List<JsValue> values)
    {
        var result = new JsObject(prototype: null);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var desc = PropertyDescriptor.Data(values[i], writable: true, enumerable: true, configurable: true);
            if (key.IsSymbol)
            {
                result.DefineOwnProperty(key.AsSymbol, desc);
            }
            else
            {
                result.DefineOwnProperty(key.AsString, desc);
            }
        }
        return JsValue.Object(result);
    }

    // ====================================================================
    //                              Helpers
    // ====================================================================

    /// <summary>§27.2.4.1.1 step 1 GetPromiseResolve — read `resolve` from C
    /// once, observably; non-callable is a TypeError.</summary>
    private static JsValue GetPromiseResolve(JsRealm realm, JsValue c)
    {
        var resolve = AbstractOperations.Get(realm.ActiveVm, c.AsObject, "resolve");
        if (!AbstractOperations.IsCallable(resolve))
        {
            throw new JsThrow(realm.NewTypeError("Promise resolve is not a function"));
        }

        return resolve;
    }

    /// <summary>§27.2.1.5 NewPromiseCapability, specialized for the
    /// built-in Promise constructor.</summary>
    private static PromiseCapability NewPromiseCapability(JsRealm realm)
    {
        var promise = new JsPromise(realm.PromisePrototype);
        var (resolve, reject) = CreateResolvingFunctions(realm, promise);
        return new PromiseCapability(promise, resolve, reject);
    }

    /// <summary>§27.2.1.5 NewPromiseCapability(C) — the generic form: a
    /// Promise subclass (or any constructor with Promise-compatible executor
    /// semantics) builds the result promise, so `Promise.all.call(Sub, …)`
    /// yields a Sub instance. The realm's own %Promise% takes the fast
    /// path.</summary>
    private static PromiseCapability NewPromiseCapability(JsRealm realm, JsValue c)
    {
        if (c.IsObject && ReferenceEquals(c.AsObject, realm.PromiseConstructor))
        {
            return NewPromiseCapability(realm);
        }

        if (!AbstractOperations.IsConstructor(c))
        {
            throw new JsThrow(realm.NewTypeError("Promise capability requires a constructor"));
        }

        var vm = realm.ActiveVm;
        // slots[0] = resolve, slots[1] = reject — the GetCapabilitiesExecutor
        // records. A repeat call is a TypeError only once either slot holds a
        // non-undefined value (§27.2.1.5.1 steps 1–2).
        var slots = new JsValue[2] { JsValue.Undefined, JsValue.Undefined };
        var executor = new JsNativeFunction(realm, "", 2, (_, eargs) =>
        {
            if (!slots[0].IsUndefined || !slots[1].IsUndefined)
            {
                throw new JsThrow(realm.NewTypeError("Promise executor already invoked with non-undefined fields"));
            }

            slots[0] = eargs.Length > 0 ? eargs[0] : JsValue.Undefined;
            slots[1] = eargs.Length > 1 ? eargs[1] : JsValue.Undefined;
            return JsValue.Undefined;
        }, isConstructor: false);
        var promiseV = AbstractOperations.Construct(vm, c, new[] { JsValue.Object(executor) });
        if (!AbstractOperations.IsCallable(slots[0]) || !AbstractOperations.IsCallable(slots[1]))
        {
            throw new JsThrow(realm.NewTypeError("Promise executor did not receive callable resolving functions"));
        }

        if (!promiseV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Promise constructor did not return an object"));
        }

        return new PromiseCapability(promiseV.AsObject, slots[0], slots[1]);
    }

    /// <summary>Build a real AggregateError instance via
    /// <c>realm.NewAggregateError</c> and attach the rejection reasons
    /// as an own <c>errors</c> array. Used by §27.2.4.3
    /// Promise.any when every input rejects.</summary>
    private static JsValue MakeAggregateError(JsRealm realm, IReadOnlyList<JsValue> errors, string message)
    {
        var errValue = realm.NewAggregateError(message);
        var errorsArray = new JsArray(realm, errors);
        errValue.AsObject.DefineOwnProperty("errors",
            PropertyDescriptor.Data(JsValue.Object(errorsArray), writable: true, enumerable: false, configurable: true));
        return errValue;
    }
}
