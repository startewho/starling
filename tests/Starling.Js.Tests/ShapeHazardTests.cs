using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// Tricky property-semantics scenarios that stress the hidden-class "shapes" +
/// slot-array storage refactor. Each test evaluates a self-contained JS snippet
/// and compares against what a real JS engine (V8 / Node.js) produces.
/// </summary>
[TestClass]
public class ShapeHazardTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    // -----------------------------------------------------------------------
    // 1. Property insertion ORDER
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Key_order_all_string_keys_preserves_insertion_order()
    {
        Eval("Object.keys({b:1,a:2,c:3}).join(',');")
            .AsString.Should().Be("b,a,c");
    }

    [TestMethod]
    public void Key_order_integer_indices_hoist_ascending_before_string_keys()
    {
        Eval("Object.keys({2:'x',1:'y',b:'z',0:'w'}).join(',');")
            .AsString.Should().Be("0,1,2,b");
    }

    [TestMethod]
    public void Key_order_mixed_indices_and_strings()
    {
        // Integer keys (0,1,2) first ascending, then strings in creation order (b,a)
        Eval("Object.keys({b:0, 2:0, a:0, 1:0, 0:0}).join(',');")
            .AsString.Should().Be("0,1,2,b,a");
    }

    [TestMethod]
    public void Key_order_reflect_ownKeys_integers_then_strings_then_symbols()
    {
        Eval(@"
            var s = Symbol('s');
            var o = {};
            o[s] = 1;
            o['b'] = 2;
            o[2] = 3;
            o['a'] = 4;
            o[1] = 5;
            Reflect.ownKeys(o).map(function(k){
                return typeof k === 'symbol' ? 'SYM' : String(k);
            }).join(',');
        ").AsString.Should().Be("1,2,b,a,SYM");
    }

    // -----------------------------------------------------------------------
    // 2. DELETE then re-add moves key to END
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Delete_then_readd_moves_key_to_end()
    {
        Eval(@"
            var o = {a:1, b:2, c:3};
            delete o.b;
            o.b = 9;
            Object.keys(o).join(',');
        ").AsString.Should().Be("a,c,b");
    }

    [TestMethod]
    public void Delete_first_then_readd_moves_to_end()
    {
        Eval(@"
            var o = {a:1, b:2, c:3};
            delete o.a;
            o.a = 10;
            Object.keys(o).join(',');
        ").AsString.Should().Be("b,c,a");
    }

    [TestMethod]
    public void Delete_two_keys_and_readd_both_in_order()
    {
        Eval(@"
            var o = {p1:1, p2:2, p3:3, p4:4};
            delete o.p1;
            delete o.p3;
            o.p1 = 10;
            Object.keys(o).join(',');
        ").AsString.Should().Be("p2,p4,p1");
    }

    // -----------------------------------------------------------------------
    // 3. delete return values and non-configurable
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Delete_existing_configurable_property_returns_true()
    {
        Eval("var o = {a:1}; delete o.a;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Delete_non_existing_property_returns_true()
    {
        Eval("var o = {}; delete o.x;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Delete_non_configurable_property_returns_false_sloppy()
    {
        Eval(@"
            var o = {};
            Object.defineProperty(o, 'x', {value:1, configurable:false});
            delete o.x;
        ").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Delete_non_configurable_property_throws_in_strict_mode()
    {
        Action act = () => Eval(@"
            'use strict';
            var o = {};
            Object.defineProperty(o, 'x', {value:1, configurable:false});
            delete o.x;
        ");
        act.Should().Throw<JsThrow>();
    }

    // -----------------------------------------------------------------------
    // 4. Object.defineProperty
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DefineProperty_non_enumerable_excluded_from_keys_but_in_getOwnPropertyNames()
    {
        Eval(@"
            var o = {a:1};
            Object.defineProperty(o, 'b', {value:2, enumerable:false, configurable:true, writable:true});
            var keys = Object.keys(o).join(',');
            var names = Object.getOwnPropertyNames(o).join(',');
            keys + '|' + names;
        ").AsString.Should().Be("a|a,b");
    }

    [TestMethod]
    public void DefineProperty_writable_false_silent_in_sloppy()
    {
        Eval(@"
            var o = {};
            Object.defineProperty(o, 'x', {value:1, writable:false, configurable:true, enumerable:true});
            o.x = 2;
            o.x;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void DefineProperty_writable_false_throws_in_strict()
    {
        Action act = () => Eval(@"
            'use strict';
            var o = {};
            Object.defineProperty(o, 'x', {value:1, writable:false, configurable:true, enumerable:true});
            o.x = 2;
        ");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void DefineProperty_redefining_non_configurable_throws()
    {
        Action act = () => Eval(@"
            var o = {};
            Object.defineProperty(o, 'x', {value:1, writable:false, configurable:false, enumerable:false});
            Object.defineProperty(o, 'x', {value:2});
        ");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void DefineProperty_data_to_accessor_conversion()
    {
        Eval(@"
            var o = {x:1};
            Object.defineProperty(o, 'x', {get: function(){ return 42; }, configurable:true});
            o.x;
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void DefineProperty_accessor_to_data_conversion()
    {
        Eval(@"
            var o = {};
            Object.defineProperty(o, 'x', {get: function(){ return 99; }, configurable:true});
            Object.defineProperty(o, 'x', {value:7, writable:true, configurable:true, enumerable:true});
            o.x;
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void DefineProperty_partial_descriptor_preserves_enumerable()
    {
        Eval(@"
            var o = {a:1};
            Object.defineProperty(o, 'a', {value:11});
            var d = Object.getOwnPropertyDescriptor(o, 'a');
            String(d.enumerable) + '|' + d.value;
        ").AsString.Should().Be("true|11");
    }

    // -----------------------------------------------------------------------
    // 5. Getters / setters
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Getter_returns_correct_value()
    {
        Eval("var o = {get x(){ return 42; }}; o.x;")
            .AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Setter_side_effects_observed()
    {
        Eval(@"
            var seen = 0;
            var o = {set x(v){ seen = v; }};
            o.x = 7;
            seen;
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Get_set_pair_on_same_key()
    {
        Eval(@"
            var val = 0;
            var o = { get x(){ return val; }, set x(v){ val = v * 2; } };
            o.x = 5;
            o.x;
        ").AsNumber.Should().Be(10);
    }

    [TestMethod]
    public void DefineProperty_accessor_on_existing_key()
    {
        Eval(@"
            var o = {};
            Object.defineProperty(o, 'y', {
                get: function(){ return 100; },
                configurable: true, enumerable: true
            });
            o.y;
        ").AsNumber.Should().Be(100);
    }

    [TestMethod]
    public void Inherited_getter_called_with_correct_this()
    {
        Eval(@"
            var proto = { get tag(){ return this._tag; } };
            var o = Object.create(proto);
            o._tag = 'hello';
            o.tag;
        ").AsString.Should().Be("hello");
    }

    // -----------------------------------------------------------------------
    // 6. Object.freeze, seal, preventExtensions
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Freeze_rejects_write_sloppy()
    {
        Eval(@"
            var o = {a:1};
            Object.freeze(o);
            o.a = 2;
            o.a;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Freeze_rejects_write_strict()
    {
        Action act = () => Eval(@"
            'use strict';
            var o = {a:1};
            Object.freeze(o);
            o.a = 2;
        ");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Freeze_isFrozen_true()
    {
        Eval("var o = {a:1}; Object.freeze(o); Object.isFrozen(o);")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Freeze_rejects_add_new_property_sloppy()
    {
        Eval(@"
            var o = {};
            Object.freeze(o);
            o.newProp = 42;
            'newProp' in o;
        ").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Seal_isSealed_true_and_rejects_delete()
    {
        Eval(@"
            var o = {a:1};
            Object.seal(o);
            var sealed = Object.isSealed(o);
            var delResult = delete o.a;
            sealed + ',' + delResult + ',' + o.a;
        ").AsString.Should().Be("true,false,1");
    }

    [TestMethod]
    public void PreventExtensions_rejects_add_property_sloppy()
    {
        Eval(@"
            var o = {a:1};
            Object.preventExtensions(o);
            o.b = 2;
            'b' in o;
        ").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void PreventExtensions_isExtensible_false()
    {
        Eval("var o = {}; Object.preventExtensions(o); Object.isExtensible(o);")
            .AsBool.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // 7. Symbols
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Symbol_keyed_props_not_in_Object_keys()
    {
        Eval(@"
            var s = Symbol('test');
            var o = {a:1};
            o[s] = 2;
            Object.keys(o).join(',');
        ").AsString.Should().Be("a");
    }

    [TestMethod]
    public void GetOwnPropertySymbols_returns_symbol_keys()
    {
        Eval(@"
            var s = Symbol('x');
            var o = {};
            o[s] = 42;
            var syms = Object.getOwnPropertySymbols(o);
            syms.length + ',' + (syms[0] === s);
        ").AsString.Should().Be("1,true");
    }

    [TestMethod]
    public void Reflect_ownKeys_order_integer_string_symbol()
    {
        Eval(@"
            var s1 = Symbol('s1');
            var s2 = Symbol('s2');
            var o = {};
            o[s1] = 1;
            o['b'] = 2;
            o[2] = 3;
            o[s2] = 4;
            o['a'] = 5;
            o[1] = 6;
            Reflect.ownKeys(o).map(function(k){
                return typeof k === 'symbol' ? 'SYM' : String(k);
            }).join(',');
        ").AsString.Should().Be("1,2,b,a,SYM,SYM");
    }

    // -----------------------------------------------------------------------
    // 8. in, hasOwnProperty, Object.hasOwn, propertyIsEnumerable
    // -----------------------------------------------------------------------

    [TestMethod]
    public void In_operator_finds_own_and_inherited_property()
    {
        Eval(@"
            var proto = {inherited:1};
            var o = Object.create(proto);
            o.own = 2;
            ('own' in o) + ',' + ('inherited' in o) + ',' + ('missing' in o);
        ").AsString.Should().Be("true,true,false");
    }

    [TestMethod]
    public void HasOwnProperty_only_own_not_inherited()
    {
        Eval(@"
            var proto = {inherited:1};
            var o = Object.create(proto);
            o.own = 2;
            o.hasOwnProperty('own') + ',' + o.hasOwnProperty('inherited');
        ").AsString.Should().Be("true,false");
    }

    [TestMethod]
    public void Object_hasOwn_equivalent_to_hasOwnProperty()
    {
        Eval(@"
            var proto = {inherited:1};
            var o = Object.create(proto);
            o.own = 2;
            Object.hasOwn(o,'own') + ',' + Object.hasOwn(o,'inherited');
        ").AsString.Should().Be("true,false");
    }

    [TestMethod]
    public void PropertyIsEnumerable_false_for_non_enumerable_prop()
    {
        Eval(@"
            var o = {};
            Object.defineProperty(o, 'x', {value:1, enumerable:false, configurable:true, writable:true});
            o.propertyIsEnumerable('x');
        ").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void PropertyIsEnumerable_false_for_inherited_prop()
    {
        Eval(@"
            var proto = {a:1};
            var o = Object.create(proto);
            o.propertyIsEnumerable('a');
        ").AsBool.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // 9. Prototype chain
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Read_inherited_data_property_through_prototype_chain()
    {
        Eval(@"
            var proto = {x:99};
            var o = Object.create(proto);
            o.x;
        ").AsNumber.Should().Be(99);
    }

    [TestMethod]
    public void Own_property_shadows_inherited()
    {
        Eval(@"
            var proto = {x:1};
            var o = Object.create(proto);
            o.x = 2;
            o.x;
        ").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Assigning_to_object_with_inherited_non_writable_prop_is_rejected_sloppy()
    {
        Eval(@"
            var proto = {};
            Object.defineProperty(proto, 'x', {value:1, writable:false, configurable:false});
            var o = Object.create(proto);
            o.x = 2;
            'x' in o && !o.hasOwnProperty('x');
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Assigning_to_object_with_inherited_non_writable_prop_throws_strict()
    {
        Action act = () => Eval(@"
            'use strict';
            var proto = {};
            Object.defineProperty(proto, 'x', {value:1, writable:false, configurable:false});
            var o = Object.create(proto);
            o.x = 2;
        ");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Object_create_null_has_no_prototype()
    {
        Eval("Object.getPrototypeOf(Object.create(null)) === null;")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Object_setPrototypeOf_changes_prototype()
    {
        Eval(@"
            var newProto = {magic:77};
            var o = {};
            Object.setPrototypeOf(o, newProto);
            o.magic;
        ").AsNumber.Should().Be(77);
    }

    // -----------------------------------------------------------------------
    // 10. Object.assign, spread, JSON.stringify order, Object.entries/values
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Object_assign_copies_enumerable_own_string_and_symbol_props()
    {
        Eval(@"
            var s = Symbol('s');
            var src = {a:1};
            src[s] = 2;
            var tgt = Object.assign({}, src);
            tgt.a + ',' + tgt[s];
        ").AsString.Should().Be("1,2");
    }

    [TestMethod]
    public void Spread_copies_enumerable_own_string_and_symbol_props()
    {
        Eval(@"
            var s = Symbol('s');
            var src = {a:1};
            src[s] = 2;
            var tgt = {...src};
            tgt.a + ',' + tgt[s];
        ").AsString.Should().Be("1,2");
    }

    [TestMethod]
    public void JSON_stringify_key_order_integers_first_then_strings()
    {
        // JSON.stringify uses [[OwnPropertyKeys]] order for plain objects
        Eval("JSON.stringify({b:1, 1:2, a:3, 0:4});")
            .AsString.Should().Be("{\"0\":4,\"1\":2,\"b\":1,\"a\":3}");
    }

    [TestMethod]
    public void Object_entries_order_matches_Object_keys_order()
    {
        Eval(@"
            var o = {b:1, 1:2, a:3};
            Object.entries(o).map(function(e){ return e[0]+':'+e[1]; }).join(',');
        ").AsString.Should().Be("1:2,b:1,a:3");
    }

    [TestMethod]
    public void Object_values_order_matches_Object_keys_order()
    {
        Eval(@"
            var o = {b:10, 1:20, a:30};
            Object.values(o).join(',');
        ").AsString.Should().Be("20,10,30");
    }

    // -----------------------------------------------------------------------
    // 11. Many properties — exercise slot-array growth
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Fifty_props_in_loop_all_readable_and_sum_correct()
    {
        Eval(@"
            var o = {};
            for (var i = 0; i < 50; i++) {
                o['prop' + i] = i;
            }
            var sum = 0;
            for (var j = 0; j < 50; j++) {
                sum += o['prop' + j];
            }
            sum;
        ").AsNumber.Should().Be(1225); // 0+1+...+49 = 1225
    }

    [TestMethod]
    public void Fifty_integer_index_props_all_readable()
    {
        Eval(@"
            var o = {};
            for (var i = 0; i < 50; i++) {
                o[i] = i * 2;
            }
            var sum = 0;
            for (var j = 0; j < 50; j++) {
                sum += o[j];
            }
            sum;
        ").AsNumber.Should().Be(2450); // 2*(0+1+...+49) = 2*1225 = 2450
    }

    // -----------------------------------------------------------------------
    // 12. Same-shape objects share the shape correctly
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Two_literals_with_same_shape_read_back_correctly()
    {
        Eval(@"
            var a = {x:1, y:2};
            var b = {x:3, y:4};
            a.x + ',' + a.y + ',' + b.x + ',' + b.y;
        ").AsString.Should().Be("1,2,3,4");
    }

    [TestMethod]
    public void Constructor_function_shape_reads_back_correctly()
    {
        Eval(@"
            function P(a, b) { this.x = a; this.y = b; }
            var p = new P(1, 2);
            p.x + ',' + p.y;
        ").AsString.Should().Be("1,2");
    }

    [TestMethod]
    public void Multiple_constructor_instances_have_independent_slots()
    {
        Eval(@"
            function P(a, b) { this.x = a; this.y = b; }
            var p1 = new P(1, 2);
            var p2 = new P(10, 20);
            p1.x + ',' + p1.y + ',' + p2.x + ',' + p2.y;
        ").AsString.Should().Be("1,2,10,20");
    }

    // -----------------------------------------------------------------------
    // 13. Property reassignment via =
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Reassign_property_reads_new_value()
    {
        Eval(@"
            var o = {a:1};
            o.a = 2;
            o.a;
        ").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Reassign_property_many_times_reads_latest_value()
    {
        Eval(@"
            var o = {a:0};
            for (var i = 1; i <= 20; i++) {
                o.a = i;
            }
            o.a;
        ").AsNumber.Should().Be(20);
    }

    [TestMethod]
    public void Reassign_multiple_properties_all_updated()
    {
        Eval(@"
            var o = {x:1, y:2, z:3};
            o.x = 10;
            o.y = 20;
            o.z = 30;
            o.x + ',' + o.y + ',' + o.z;
        ").AsString.Should().Be("10,20,30");
    }

    // -----------------------------------------------------------------------
    // 14. Proxy traps
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Proxy_get_trap_fires_on_property_read()
    {
        Eval(@"
            var hits = 0;
            var p = new Proxy({a:1}, {
                get: function(t, k) { hits++; return t[k]; }
            });
            var _ = p.a;
            hits;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Proxy_set_trap_fires_on_property_write()
    {
        Eval(@"
            var log = [];
            var p = new Proxy({}, {
                set: function(t, k, v) { log.push(k+'='+v); t[k]=v; return true; }
            });
            p.x = 5;
            p.y = 10;
            log.join(',');
        ").AsString.Should().Be("x=5,y=10");
    }

    [TestMethod]
    public void Proxy_has_trap_fires_for_in_operator()
    {
        Eval(@"
            var hits = 0;
            var p = new Proxy({}, {
                has: function(t, k) { hits++; return true; }
            });
            'anything' in p;
            hits;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Proxy_deleteProperty_trap_fires_for_delete_operator()
    {
        Eval(@"
            var deletedKey = null;
            var p = new Proxy({a:1}, {
                deleteProperty: function(t, k) { deletedKey = k; delete t[k]; return true; }
            });
            delete p.a;
            deletedKey;
        ").AsString.Should().Be("a");
    }

    [TestMethod]
    public void Proxy_ownKeys_trap_fires_for_Object_keys()
    {
        Eval(@"
            var p = new Proxy({}, {
                ownKeys: function() { return ['x', 'y']; },
                getOwnPropertyDescriptor: function(t, k) {
                    return { value:1, writable:true, enumerable:true, configurable:true };
                }
            });
            Object.keys(p).join(',');
        ").AsString.Should().Be("x,y");
    }

    // -----------------------------------------------------------------------
    // 15. arguments object (mapped, sloppy)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Sloppy_mapped_arguments_write_updates_parameter()
    {
        Eval("function f(a){ arguments[0] = 9; return a; } f(1);")
            .AsNumber.Should().Be(9);
    }

    [TestMethod]
    public void Sloppy_parameter_write_updates_arguments()
    {
        Eval("function f(a){ a = 7; return arguments[0]; } f(1);")
            .AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Strict_arguments_not_linked_to_parameter()
    {
        Eval("function f(a){ 'use strict'; arguments[0] = 9; return a; } f(1);")
            .AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Non_simple_params_arguments_not_linked()
    {
        // Default parameter makes it non-simple => unmapped
        Eval("function f(a = 1){ arguments[0] = 9; return a; } f(2);")
            .AsNumber.Should().Be(2);
    }
}
