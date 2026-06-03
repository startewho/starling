using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// Adversarial tests targeting the one-hop inherited-property READ cache and
/// the add-new-property WRITE cache introduced with prototype-chain inline
/// caches. Each test exercises a call site many times so the cache fills,
/// then tries to trip a stale or wrong cached result.
/// </summary>
[TestClass]
public class InlineCacheProtoHazardTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    // =========================================================================
    // 1. POLYMORPHIC PROTOTYPES at one READ site
    // =========================================================================

    /// <summary>
    /// Same receiver shape, four different prototypes — fill the cache on
    /// proto-with-value, then hit with proto-with-getter, proto-with-nothing,
    /// and own-shadow variants.
    /// </summary>
    [TestMethod]
    public void Poly_read_same_receiver_shape_different_proto_value_vs_getter_vs_missing_vs_own()
    {
        // Each call site sees the SAME receiver shape {z:0} but different prototypes.
        Eval(@"
            function read(o) { return o.x; }

            // proto1: x as plain value
            var proto1 = { x: 10 };
            // proto2: x as a getter
            var proto2 = Object.create(Object.prototype);
            Object.defineProperty(proto2, 'x', { get: function() { return 20; }, configurable: true });
            // proto3: no x at all
            var proto3 = {};
            // proto4: receiver has own x (shadow)
            var proto4 = { x: 999 };

            var a = Object.create(proto1); a.z = 0;
            var b = Object.create(proto2); b.z = 0;
            var c = Object.create(proto3); c.z = 0;
            var d = Object.create(proto4); d.z = 0; d.x = 40;  // own x shadows proto

            // Warm up with proto1 objects (fills cache with proto-read ic)
            var sum = 0;
            for (var i = 0; i < 100; i++) sum += read(a);

            // Now exercise all variants
            var r1 = read(a);   // expects 10
            var r2 = read(b);   // expects 20 (getter)
            var r3 = read(c);   // expects undefined
            var r4 = read(d);   // expects 40 (own shadow)

            // Confirm: 10 + 20 + 0 + 40 = 70; undefined coerces to 0 via Number
            r1 + '|' + r2 + '|' + r3 + '|' + r4;
        ").AsString.Should().Be("10|20|undefined|40");
    }

    [TestMethod]
    public void Poly_read_getter_first_then_plain_value()
    {
        // Fill cache with a getter holder, then switch to a plain-value holder.
        Eval(@"
            function read(o) { return o.x; }
            var protoGetter = Object.create(Object.prototype);
            Object.defineProperty(protoGetter, 'x', { get: function() { return 77; }, configurable: true });
            var protoVal = { x: 55 };

            var a = Object.create(protoGetter); a.z = 0;
            var b = Object.create(protoVal);    b.z = 0;

            // warm on getter path
            for (var i = 0; i < 100; i++) read(a);

            read(a) + '|' + read(b);
        ").AsString.Should().Be("77|55");
    }

    [TestMethod]
    public void Poly_read_all_have_missing_x_but_one()
    {
        Eval(@"
            function read(o) { return o.x; }
            var base = {};
            var a = Object.create(base); a.z = 0;
            var b = Object.create(base); b.z = 0;
            var c = Object.create(base); c.z = 0;
            // Warm cache: all miss (undefined)
            for (var i = 0; i < 60; i++) { read(a); read(b); }
            // Now add x to base AFTER cache is warm — should NOT be cached stale
            base.x = 99;
            read(c);
        ").AsNumber.Should().Be(99);
    }

    [TestMethod]
    public void Poly_read_two_protos_alternating()
    {
        // Alternating between two protos with different x values — cache must
        // not lock onto one value.
        Eval(@"
            function read(o) { return o.x; }
            var p1 = { x: 1 };
            var p2 = { x: 2 };
            var a = Object.create(p1); a.z = 0;
            var b = Object.create(p2); b.z = 0;

            var last = 0;
            for (var i = 0; i < 200; i++) {
                last = read(i % 2 === 0 ? a : b);
            }
            // last iteration i=199 (odd) => b => 2
            last;
        ").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Poly_read_sum_alternating_two_protos()
    {
        Eval(@"
            function read(o) { return o.x; }
            var p1 = { x: 3 };
            var p2 = { x: 7 };
            var a = Object.create(p1); a.z = 0;
            var b = Object.create(p2); b.z = 0;

            var sum = 0;
            for (var i = 0; i < 100; i++) {
                sum += read(a) + read(b);
            }
            // 100 * (3 + 7) = 1000
            sum;
        ").AsNumber.Should().Be(1000);
    }

    // =========================================================================
    // 2. POLYMORPHIC PROTOTYPES at one WRITE site
    // =========================================================================

    [TestMethod]
    public void Poly_write_proto_with_inherited_setter_must_call_setter_not_create_own()
    {
        // After cache warms on "plain add" path, switch to object whose proto has
        // an inherited setter — must invoke the setter, not bypass it.
        Eval(@"
            function write(o) { o.x = 1; return o.hasOwnProperty('x'); }

            var protoPlain = {};
            var sideEffect = 0;
            var protoSetter = Object.create(Object.prototype);
            Object.defineProperty(protoSetter, 'x', {
                set: function(v) { sideEffect = v; },
                get: function()  { return sideEffect; },
                configurable: true
            });

            var a = Object.create(protoPlain);  // plain add — creates own x
            var b = Object.create(protoSetter); // setter — must NOT create own

            // Warm cache on plain add path
            for (var i = 0; i < 100; i++) {
                var tmp = Object.create(protoPlain);
                write(tmp);
            }

            var ownAfterPlain  = write(a);  // true: plain add creates own property
            var ownAfterSetter = write(b);  // false: setter must NOT create own

            ownAfterPlain + '|' + ownAfterSetter + '|' + sideEffect;
        ").AsString.Should().Be("true|false|1");
    }

    [TestMethod]
    public void Poly_write_proto_with_non_writable_inherited_data_must_not_create_own_sloppy()
    {
        // Inherited non-writable data property: sloppy write is silently ignored.
        Eval(@"
            function write(o) { o.x = 99; return o.hasOwnProperty('x') ? o.x : 'no-own'; }

            var protoFrozenX = {};
            Object.defineProperty(protoFrozenX, 'x', { value: 1, writable: false, configurable: false, enumerable: true });

            var protoNormal = {};

            // Warm on normal add
            for (var i = 0; i < 80; i++) {
                var tmp = Object.create(protoNormal);
                write(tmp);
            }

            // Now try with non-writable inherited x — should silently ignore (sloppy)
            var b = Object.create(protoFrozenX);
            write(b);
        ").AsString.Should().Be("no-own");
    }

    [TestMethod]
    public void Poly_write_inherited_writable_data_creates_own_shadow()
    {
        // Writable inherited data: write CREATES own shadow (spec §10.1.9.1).
        Eval(@"
            function write(o, v) { o.x = v; return o.hasOwnProperty('x') ? o.x : 'inherited'; }

            var protoWithX = { x: 0 };
            var a = Object.create(protoWithX);

            for (var i = 0; i < 80; i++) write(a, i);

            // After 80 writes a has its own x; last value is 79
            write(a, 99);
        ").AsNumber.Should().Be(99);
    }

    [TestMethod]
    public void Write_cache_add_then_setter_on_same_prototype_after_warmup()
    {
        // The write cache locks in an add-transition for a prototype with no x.
        // Midway we add a setter for x on that prototype.
        // After that, new objects must invoke the setter, not bypass it.
        Eval(@"
            var proto = {};
            var log = [];

            function makeAndWrite(v) {
                var o = Object.create(proto);
                o.x = v;
                return o.hasOwnProperty('x');
            }

            // Warm: no x on proto, cache fills as add-transition
            for (var i = 0; i < 80; i++) makeAndWrite(i);

            // Install setter on proto — bumps epoch
            Object.defineProperty(proto, 'x', {
                set: function(v) { log.push(v); },
                get: function()  { return log[log.length - 1]; },
                configurable: true
            });

            // Now new objects should invoke setter, not create own property
            var ownResult = makeAndWrite(42);
            ownResult + '|' + log[0];
        ").AsString.Should().Be("false|42");
    }

    // =========================================================================
    // 3. EPOCH INVALIDATION on READ
    // =========================================================================

    [TestMethod]
    public void Epoch_read_mutate_proto_midway_sees_new_value()
    {
        Eval(@"
            function read(o) { return o.m; }
            var proto = { m: 1 };
            var o = Object.create(proto);
            o.z = 0;  // give receiver a non-empty shape

            // Warm cache
            for (var i = 0; i < 100; i++) read(o);

            // Mutate m on the prototype (should bump epoch)
            proto.m = 999;

            read(o);
        ").AsNumber.Should().Be(999);
    }

    [TestMethod]
    public void Epoch_read_delete_then_readd_sees_new_value()
    {
        Eval(@"
            function read(o) { return o.m; }
            var proto = { m: 1 };
            var o = Object.create(proto);
            o.z = 0;

            for (var i = 0; i < 100; i++) read(o);

            delete proto.m;
            proto.m = 42;

            read(o);
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Epoch_read_delete_property_from_proto_returns_undefined()
    {
        Eval(@"
            function read(o) { return o.m; }
            var proto = { m: 7 };
            var o = Object.create(proto);
            o.z = 0;

            for (var i = 0; i < 100; i++) read(o);

            delete proto.m;

            String(read(o));
        ").AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void Epoch_read_redefine_as_getter_after_warmup()
    {
        Eval(@"
            function read(o) { return o.m; }
            var proto = { m: 5 };
            var o = Object.create(proto);
            o.z = 0;

            for (var i = 0; i < 100; i++) read(o);

            // Redefine m as a getter — forces proto into dict mode, bumps epoch
            Object.defineProperty(proto, 'm', {
                get: function() { return 55; },
                configurable: true
            });

            read(o);
        ").AsNumber.Should().Be(55);
    }

    [TestMethod]
    public void Epoch_read_multiple_proto_mutations_each_reflected()
    {
        Eval(@"
            function read(o) { return o.v; }
            var proto = { v: 0 };
            var o = Object.create(proto); o.z = 0;

            var results = [];
            for (var i = 1; i <= 5; i++) {
                // Warm N times, then mutate
                for (var j = 0; j < 20; j++) read(o);
                proto.v = i * 10;
                results.push(read(o));
            }
            results.join(',');
        ").AsString.Should().Be("10,20,30,40,50");
    }

    // =========================================================================
    // 4. EPOCH INVALIDATION on ADD (write cache)
    // =========================================================================

    [TestMethod]
    public void Epoch_add_cache_invalidated_when_inherited_setter_added()
    {
        Eval(@"
            var proto = {};
            var log = [];

            // Warm cache: straightforward add-transition (no x on proto)
            for (var i = 0; i < 80; i++) {
                var tmp = Object.create(proto);
                tmp.x = i;
            }

            // Install inherited setter for x
            Object.defineProperty(proto, 'x', {
                set: function(v) { log.push(v); },
                configurable: true
            });

            // New adds must use setter, not fast-path transition
            var o1 = Object.create(proto); o1.x = 101;
            var o2 = Object.create(proto); o2.x = 102;

            log.join(',');
        ").AsString.Should().Be("101,102");
    }

    [TestMethod]
    public void Epoch_add_cache_invalidated_by_non_writable_inherited_property()
    {
        // After adding a non-writable inherited x, the add must be silently
        // rejected in sloppy mode — not bypass via stale cache.
        Eval(@"
            var proto = {};

            for (var i = 0; i < 80; i++) {
                var tmp = Object.create(proto);
                tmp.x = i;
            }

            // Add non-writable x to proto
            Object.defineProperty(proto, 'x', { value: 0, writable: false, configurable: false, enumerable: true });

            var o = Object.create(proto);
            o.x = 123; // sloppy: silently ignored
            o.hasOwnProperty('x');
        ").AsBool.Should().BeFalse();
    }

    // =========================================================================
    // 5. __proto__ / Object.setPrototypeOf swap
    // =========================================================================

    [TestMethod]
    public void SetPrototypeOf_swap_read_reflects_new_proto()
    {
        Eval(@"
            function read(o) { return o.x; }
            var p1 = { x: 1 };
            var p2 = { x: 2 };
            var o = Object.create(p1); o.z = 0;

            for (var i = 0; i < 100; i++) read(o);

            Object.setPrototypeOf(o, p2);

            read(o);
        ").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void SetPrototypeOf_swap_to_proto_without_property_returns_undefined()
    {
        Eval(@"
            function read(o) { return o.x; }
            var p1 = { x: 10 };
            var p2 = {};
            var o = Object.create(p1); o.z = 0;

            for (var i = 0; i < 100; i++) read(o);

            Object.setPrototypeOf(o, p2);

            String(read(o));
        ").AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void SetPrototypeOf_back_and_forth_always_correct()
    {
        Eval(@"
            function read(o) { return o.x; }
            var p1 = { x: 100 };
            var p2 = { x: 200 };
            var o = Object.create(p1); o.z = 0;

            var results = [];
            for (var i = 0; i < 10; i++) {
                for (var j = 0; j < 10; j++) read(o);
                Object.setPrototypeOf(o, i % 2 === 0 ? p2 : p1);
                results.push(read(o));
            }
            results.join(',');
        ").AsString.Should().Be("200,100,200,100,200,100,200,100,200,100");
    }

    // =========================================================================
    // 6. Inherited DATA read returns LIVE value
    // =========================================================================

    [TestMethod]
    public void Live_proto_value_reflected_after_mutation()
    {
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 0 };
            var o = Object.create(proto); o.z = 0;

            var results = [];
            for (var i = 0; i < 50; i++) {
                proto.x = i;
                results.push(read(o));
            }
            results[49];
        ").AsNumber.Should().Be(49);
    }

    [TestMethod]
    public void Live_proto_value_sum_after_mutations()
    {
        // Verify every read sees the mutated value, not a stale cached one.
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 0 };
            var o = Object.create(proto); o.z = 0;

            // Warm once
            for (var i = 0; i < 50; i++) read(o);

            var sum = 0;
            for (var i = 1; i <= 10; i++) {
                proto.x = i;
                sum += read(o);
            }
            // 1+2+...+10 = 55
            sum;
        ").AsNumber.Should().Be(55);
    }

    // =========================================================================
    // 7. Own property SHADOWING after caching
    // =========================================================================

    [TestMethod]
    public void Shadow_after_proto_cache_warmed_returns_own_value()
    {
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 1 };
            var o = Object.create(proto); o.z = 0;

            // Warm the inherited-property cache
            for (var i = 0; i < 100; i++) read(o);

            // Install own property — must shadow cached proto slot
            o.x = 999;

            read(o);
        ").AsNumber.Should().Be(999);
    }

    [TestMethod]
    public void Shadow_then_delete_own_falls_back_to_proto()
    {
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 7 };
            var o = Object.create(proto); o.z = 0;

            for (var i = 0; i < 80; i++) read(o);

            o.x = 100;   // shadow
            read(o);     // should be 100

            delete o.x;  // remove shadow
            read(o);     // should fall back to proto (7)
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Shadow_after_cache_verify_both_objects_independent()
    {
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 5 };
            var a = Object.create(proto); a.z = 0;
            var b = Object.create(proto); b.z = 0;

            for (var i = 0; i < 80; i++) { read(a); read(b); }

            // Shadow only on b
            b.x = 99;

            read(a) + '|' + read(b);
        ").AsString.Should().Be("5|99");
    }

    // =========================================================================
    // 8. Constructor pattern (add-transition hot path)
    // =========================================================================

    [TestMethod]
    public void Constructor_1000_instances_sum_x_correct()
    {
        Eval(@"
            function P(a, b) { this.x = a; this.y = b; }
            var sumX = 0, sumY = 0;
            for (var i = 0; i < 1000; i++) {
                var o = new P(i, i * 2);
                sumX += o.x;
                sumY += o.y;
            }
            sumX + '|' + sumY;
        ").AsString.Should().Be("499500|999000");  // sum 0..999 = 499500; *2 = 999000
    }

    [TestMethod]
    public void Constructor_with_three_props_sum_correct()
    {
        Eval(@"
            function Q(a, b, c) { this.x = a; this.y = b; this.z = c; }
            var sx = 0, sy = 0, sz = 0;
            for (var i = 0; i < 500; i++) {
                var o = new Q(i, i + 1, i + 2);
                sx += o.x; sy += o.y; sz += o.z;
            }
            sx + '|' + sy + '|' + sz;
        ").AsString.Should().Be("124750|125250|125750");
        // sum(0..499)=124750; sum(1..500)=125250; sum(2..501)=125750
    }

    [TestMethod]
    public void Constructor_same_shape_different_values_no_bleeding()
    {
        Eval(@"
            function P(a, b) { this.x = a; this.y = b; }
            var objects = [];
            for (var i = 0; i < 100; i++) objects.push(new P(i, i * 3));
            // Verify no value bleeds between instances
            var ok = true;
            for (var j = 0; j < 100; j++) {
                if (objects[j].x !== j || objects[j].y !== j * 3) { ok = false; break; }
            }
            ok;
        ").AsBool.Should().BeTrue();
    }

    // =========================================================================
    // 9. Class getter polymorphism
    // =========================================================================

    [TestMethod]
    public void Class_getter_poly_A_vs_B_read_at_same_site()
    {
        Eval(@"
            class A { get v() { return 1; } }
            class B { get v() { return 2; } }
            function readV(o) { return o.v; }

            var a = new A();
            var b = new B();

            // Warm on A
            for (var i = 0; i < 100; i++) readV(a);

            // Now interleave
            var sum = 0;
            for (var i = 0; i < 50; i++) {
                sum += readV(a) + readV(b);
            }
            // 50 * (1+2) = 150
            sum;
        ").AsNumber.Should().Be(150);
    }

    [TestMethod]
    public void Class_getter_three_classes_poly()
    {
        Eval(@"
            class A { get v() { return 10; } }
            class B { get v() { return 20; } }
            class C { get v() { return 30; } }
            function readV(o) { return o.v; }

            var a = new A(), b = new B(), c = new C();

            var sum = 0;
            for (var i = 0; i < 30; i++) {
                sum += readV(a) + readV(b) + readV(c);
            }
            // 30 * 60 = 1800
            sum;
        ").AsNumber.Should().Be(1800);
    }

    [TestMethod]
    public void Class_inherited_method_poly_different_implementations()
    {
        Eval(@"
            class A { greet() { return 'a'; } }
            class B { greet() { return 'b'; } }
            function call(o) { return o.greet(); }

            var a = new A(), b = new B();
            for (var i = 0; i < 100; i++) call(a);

            var results = [];
            for (var i = 0; i < 10; i++) {
                results.push(call(a));
                results.push(call(b));
            }
            results.join('');
        ").AsString.Should().Be("abababababababababab");
    }

    // =========================================================================
    // 10. delete then re-read; freeze prototype then read; deep chain read
    // =========================================================================

    [TestMethod]
    public void Delete_own_after_cache_warmed_falls_back_to_proto()
    {
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 3 };
            var o = Object.create(proto); o.z = 0; o.x = 100; // own shadow

            // Warm with own x in place
            for (var i = 0; i < 80; i++) read(o);

            // Delete own x — must now fall back to proto
            delete o.x;

            read(o);
        ").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Freeze_proto_then_read_still_returns_correct_value()
    {
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 13 };
            var o = Object.create(proto); o.z = 0;

            for (var i = 0; i < 100; i++) read(o);

            Object.freeze(proto);

            read(o);
        ").AsNumber.Should().Be(13);
    }

    [TestMethod]
    public void Deep_two_hop_chain_read_not_cached_but_correct()
    {
        // The IC only caches one hop; two-hop must still work via slow path.
        Eval(@"
            function read(o) { return o.x; }
            var grandProto = { x: 77 };
            var proto = Object.create(grandProto);
            var o = Object.create(proto); o.z = 0;

            var sum = 0;
            for (var i = 0; i < 100; i++) sum += read(o);

            sum;
        ").AsNumber.Should().Be(7700);
    }

    [TestMethod]
    public void Deep_three_hop_chain_read_correct()
    {
        Eval(@"
            function read(o) { return o.x; }
            var ggp = { x: 5 };
            var gp  = Object.create(ggp);
            var p   = Object.create(gp);
            var o   = Object.create(p); o.z = 0;

            var sum = 0;
            for (var i = 0; i < 50; i++) sum += read(o);

            sum;
        ").AsNumber.Should().Be(250);
    }

    [TestMethod]
    public void Deep_chain_mutation_at_top_seen_by_leaf()
    {
        Eval(@"
            function read(o) { return o.x; }
            var top  = { x: 1 };
            var mid  = Object.create(top);
            var o    = Object.create(mid); o.z = 0;

            for (var i = 0; i < 80; i++) read(o);

            top.x = 999;

            read(o);
        ").AsNumber.Should().Be(999);
    }

    // =========================================================================
    // 11. Proxy mixed with plain object at the same read site
    // =========================================================================

    [TestMethod]
    public void Proxy_at_polymorphic_read_site_returns_correct_value()
    {
        Eval(@"
            function read(o) { return o.x; }
            var plain = { x: 7 };
            var proxy = new Proxy({ x: 0 }, {
                get: function(t, k) { return k === 'x' ? 42 : t[k]; }
            });

            // Warm on plain
            for (var i = 0; i < 100; i++) read(plain);

            // Proxy must NOT hit the cache
            var r1 = read(plain);
            var r2 = read(proxy);

            r1 + '|' + r2;
        ").AsString.Should().Be("7|42");
    }

    [TestMethod]
    public void Proxy_then_plain_plain_gets_own_slot_not_proxy_trap()
    {
        Eval(@"
            function read(o) { return o.x; }
            var proxy = new Proxy({ x: 0 }, {
                get: function(t, k) { return 99; }
            });
            var plain = { x: 5 };

            // Warm on proxy (proxy disables IC)
            for (var i = 0; i < 100; i++) read(proxy);

            // plain should still get its own slot
            read(plain);
        ").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Proxy_alternating_with_plain_both_always_correct()
    {
        Eval(@"
            function read(o) { return o.val; }
            var plain  = { val: 100 };
            var traps  = 0;
            var proxy  = new Proxy({}, {
                get: function(t, k) { if (k === 'val') { traps++; return 200; } return t[k]; }
            });

            var sumP = 0, sumQ = 0;
            for (var i = 0; i < 50; i++) {
                sumP += read(plain);
                sumQ += read(proxy);
            }
            sumP + '|' + sumQ + '|' + traps;
        ").AsString.Should().Be("5000|10000|50");
    }

    // =========================================================================
    // 12. Reassigning an EXISTING own property in a tight loop (existing-slot write cache)
    // =========================================================================

    [TestMethod]
    public void Existing_slot_write_cache_reads_back_correct_value_each_iteration()
    {
        Eval(@"
            var o = { x: 0 };
            for (var i = 0; i < 1000; i++) {
                o.x = i;
            }
            o.x;
        ").AsNumber.Should().Be(999);
    }

    [TestMethod]
    public void Existing_slot_write_cache_two_props_no_bleeding()
    {
        Eval(@"
            var o = { a: 0, b: 0 };
            for (var i = 0; i < 500; i++) {
                o.a = i;
                o.b = i * 2;
            }
            o.a + '|' + o.b;
        ").AsString.Should().Be("499|998");
    }

    [TestMethod]
    public void Existing_slot_write_cache_sum_is_correct()
    {
        Eval(@"
            var o = { x: 0 };
            var sum = 0;
            for (var i = 0; i < 100; i++) {
                o.x = i;
                sum += o.x;
            }
            // 0+1+...+99 = 4950
            sum;
        ").AsNumber.Should().Be(4950);
    }

    [TestMethod]
    public void Existing_slot_write_cache_multiple_objects_same_shape()
    {
        Eval(@"
            var objects = [];
            for (var i = 0; i < 100; i++) objects.push({ x: 0 });

            for (var i = 0; i < 100; i++) {
                objects[i].x = i + 1;
            }

            var ok = true;
            for (var j = 0; j < 100; j++) {
                if (objects[j].x !== j + 1) { ok = false; break; }
            }
            ok;
        ").AsBool.Should().BeTrue();
    }

    // =========================================================================
    // 13. Additional hazard: cache at one call site with many receiver types
    // =========================================================================

    [TestMethod]
    public void Cache_site_sees_object_with_own_x_then_inherited_x_then_undefined()
    {
        Eval(@"
            function read(o) { return o.x; }

            var withOwn      = { x: 1 };
            var withInherited = Object.create({ x: 2 }); withInherited.z = 0;
            var withNeither   = Object.create({}); withNeither.z = 0;

            for (var i = 0; i < 60; i++) read(withOwn);
            for (var i = 0; i < 60; i++) read(withInherited);
            for (var i = 0; i < 60; i++) read(withNeither);

            read(withOwn) + '|' + read(withInherited) + '|' + String(read(withNeither));
        ").AsString.Should().Be("1|2|undefined");
    }

    [TestMethod]
    public void Inherited_read_cache_not_confused_by_sibling_with_own_property()
    {
        // Two objects share a prototype. One later gets its own x.
        // The sibling's read must still return the prototype value.
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 11 };
            var a = Object.create(proto); a.z = 0;
            var b = Object.create(proto); b.z = 0;

            for (var i = 0; i < 100; i++) { read(a); read(b); }

            // Only b gets own x
            b.x = 22;

            read(a) + '|' + read(b);
        ").AsString.Should().Be("11|22");
    }

    [TestMethod]
    public void Write_cache_add_transition_gives_independent_slots_per_instance()
    {
        // Exercise the add-transition write cache 1000 times; every instance
        // must have its own independent slot value.
        Eval(@"
            var proto = {};
            var instances = [];
            for (var i = 0; i < 1000; i++) {
                var o = Object.create(proto);
                o.x = i;
                instances.push(o);
            }
            var ok = true;
            for (var j = 0; j < 1000; j++) {
                if (instances[j].x !== j) { ok = false; break; }
            }
            ok;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Read_cache_prototype_slot_not_mixed_with_own_slot_when_shapes_differ()
    {
        // Objects a and b have DIFFERENT receiver shapes but the property lives
        // on the same prototype. The cache must key off the receiver shape, not
        // just the prototype identity.
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 7 };

            var a = Object.create(proto);           // shape: Root (0 own props)
            var b = Object.create(proto); b.y = 0;  // shape: {y} (1 own prop)

            for (var i = 0; i < 80; i++) { read(a); read(b); }

            read(a) + '|' + read(b);
        ").AsString.Should().Be("7|7");
    }

    [TestMethod]
    public void Proto_mutation_epoch_bump_seen_across_different_receiver_objects()
    {
        // Multiple receivers sharing the same prototype. Mutate the prototype.
        // Every receiver's next read must see the new value.
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 1 };
            var receivers = [];
            for (var i = 0; i < 10; i++) {
                var r = Object.create(proto); r.z = 0;
                receivers.push(r);
            }

            // Warm
            for (var i = 0; i < 100; i++) read(receivers[i % 10]);

            proto.x = 99;

            var allCorrect = true;
            for (var j = 0; j < 10; j++) {
                if (read(receivers[j]) !== 99) { allCorrect = false; break; }
            }
            allCorrect;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void SetPrototypeOf_receiver_to_null_returns_undefined_for_inherited_prop()
    {
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 5 };
            var o = Object.create(proto); o.z = 0;

            for (var i = 0; i < 80; i++) read(o);

            Object.setPrototypeOf(o, null);

            String(read(o));
        ").AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void Write_add_cache_proto_change_on_receiver_invalidates_transition()
    {
        // The add-transition cache checks o.Prototype == ic.Holder. Changing the
        // receiver's prototype must invalidate the cached transition.
        Eval(@"
            var protoA = {};
            var protoB = {};
            Object.defineProperty(protoB, 'x', {
                set: function(v) { /* swallow */ },
                configurable: true
            });

            function makeAndAdd(proto) {
                var o = Object.create(proto);
                o.x = 42;
                return o.hasOwnProperty('x');
            }

            // Warm on protoA (plain add)
            for (var i = 0; i < 80; i++) makeAndAdd(protoA);

            // Switch to protoB which has a setter — must NOT bypass it
            var result = makeAndAdd(protoB);

            // protoB has setter so own x must NOT be created
            result;
        ").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Read_cache_distinguishes_receiver_shape_root_vs_one_prop()
    {
        // Two objects on the same prototype but with different shapes —
        // root shape (no own) and {y} shape. Both must return the prototype's x.
        Eval(@"
            function read(o) { return o.x; }
            var proto = { x: 42 };
            var noOwn = Object.create(proto);
            var oneOwn = Object.create(proto); oneOwn.y = 0;

            var sum = 0;
            for (var i = 0; i < 100; i++) {
                sum += read(noOwn) + read(oneOwn);
            }
            sum;
        ").AsNumber.Should().Be(8400); // 100 * (42 + 42) = 8400
    }
}
