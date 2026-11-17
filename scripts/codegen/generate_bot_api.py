#!/usr/bin/env python3
"""Generates the typed Telegram Bot API layer from a machine-readable spec.

The spec is https://github.com/PaulSonOfLars/telegram-bot-api-spec (api.json), pinned in
scripts/codegen/telegram-bot-api.json. Refresh it and re-run to follow a new Bot API release:

    curl -fL https://raw.githubusercontent.com/PaulSonOfLars/telegram-bot-api-spec/main/api.json \
        -o scripts/codegen/telegram-bot-api.json
    python3 scripts/codegen/generate_bot_api.py

Outputs:
    ShiroBot.Telegram.Model/BotApi/BotApiTypes.g.cs   every Bot API type and union
    src/BotApi/TelegramBotApi.g.cs                     one typed method per Bot API method
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = Path(__file__).resolve().parent / "telegram-bot-api.json"
TYPES_OUT = ROOT / "ShiroBot.Telegram.Model" / "BotApi" / "BotApiTypes.g.cs"
METHODS_OUT = ROOT / "src" / "BotApi" / "TelegramBotApi.g.cs"

PRIMITIVES = {"String": "string", "Integer": "long", "Float": "double", "Boolean": "bool", "True": "bool"}
VALUE_TYPES = {"long", "double", "bool", "ChatId"}
HAND_WRITTEN = {"InputFile"}
REPLY_MARKUP = ("InlineKeyboardMarkup", "ReplyKeyboardMarkup", "ReplyKeyboardRemove", "ForceReply")
CSHARP_KEYWORDS = {
    "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
    "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
    "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
    "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
    "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
    "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
    "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
}

spec = json.loads(SPEC.read_text(encoding="utf-8"))
types: dict = spec["types"]
methods: dict = spec["methods"]


def pascal(name: str) -> str:
    return "".join(part[:1].upper() + part[1:] for part in name.split("_"))


def camel(name: str) -> str:
    value = pascal(name)
    value = value[:1].lower() + value[1:]
    return "@" + value if value in CSHARP_KEYWORDS else value


def xml(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def doc(lines, indent: str, tag: str = "summary") -> list[str]:
    text = [xml(line) for line in (lines if isinstance(lines, list) else [lines]) if line]
    if not text:
        return []
    out = [f"{indent}/// <{tag}>"]
    for index, line in enumerate(text):
        out.append(f"{indent}///     {line}" + ("<br/>" if index < len(text) - 1 else ""))
    out.append(f"{indent}/// </{tag}>")
    return out


# ─── Unions ───

unions: dict[str, dict] = {}  # name -> {members, primitive, synthetic}
for name, value in types.items():
    if "subtypes" in value:
        members = [s for s in value["subtypes"] if s in types]
        unions[name] = {"members": members, "primitive": len(members) != len(value["subtypes"]),
                        "description": value["description"], "href": value["href"]}


def union_for(member_types: tuple[str, ...], owner: str, field: str) -> str:
    """Interface that covers several object types allowed for one field."""
    if tuple(sorted(member_types)) == tuple(sorted(REPLY_MARKUP)):
        name = "ReplyMarkup"
        description = ["Reply markup accepted by send and edit methods: one of InlineKeyboardMarkup, "
                       "ReplyKeyboardMarkup, ReplyKeyboardRemove or ForceReply."]
    else:
        covering = [(len(u["members"]), n) for n, u in unions.items()
                    if not u.get("synthetic") and set(member_types) <= set(u["members"])]
        if covering:
            return min(covering)[1]
        name = owner + pascal(field)
        description = [f"Values accepted by {owner}.{field}: " + ", ".join(member_types) + "."]
    unions.setdefault(name, {"members": list(member_types), "primitive": False, "synthetic": True,
                             "description": description, "href": None})
    return name


def is_upload_string(field) -> bool:
    return field["types"] == ["String"] and "attach://" in field["description"]


def map_type(type_names: list[str], owner: str, field, prefix: str = "") -> str:
    if len(type_names) > 1:
        if set(type_names) == {"Integer", "String"}:
            return prefix + "ChatId"
        if set(type_names) == {"InputFile", "String"}:
            return prefix + "InputFile"
        if all(t.startswith("Array of ") for t in type_names):
            inner = tuple(t[len("Array of "):] for t in type_names)
            return f"IReadOnlyList<{prefix}{union_for(inner, owner, field['name'])}>"
        return prefix + union_for(tuple(type_names), owner, field["name"])
    name = type_names[0]
    if name.startswith("Array of "):
        return f"IReadOnlyList<{map_type([name[len('Array of '):]], owner, field, prefix)}>"
    if name in PRIMITIVES:
        if name == "String" and field is not None and is_upload_string(field):
            return prefix + "InputFile"
        return PRIMITIVES[name]
    return prefix + name


# Resolve every field type first so synthetic unions exist before interfaces are emitted.
type_fields: dict[str, list[tuple[str, str, str, dict]]] = {}
for name, value in types.items():
    if name in HAND_WRITTEN or "subtypes" in value:
        continue
    fields = []
    for field in value.get("fields", []):
        property_name = pascal(field["name"])
        if property_name == name:
            property_name += "Value"
        csharp = map_type(field["types"], name, field)
        fields.append((property_name, csharp, field["name"], field))
    type_fields[name] = fields

method_params: dict[str, list[tuple[str, str, dict]]] = {}
for name, value in methods.items():
    method_params[name] = [(camel(f["name"]), map_type(f["types"], pascal(name), f, "Api."), f)
                           for f in value.get("fields", [])]

implements: dict[str, list[str]] = {}
for union_name, union in unions.items():
    for member in union["members"]:
        implements.setdefault(member, []).append(union_name)


def discriminator(member: str):
    for field in types[member].get("fields", []):
        match = re.search(r'(?:always|must be) "?([A-Za-z_0-9]+)"?', field["description"])
        if match and field["types"] == ["String"]:
            return field["name"], match.group(1)
    return None


def property_type(prop_type: str, required: bool) -> str:
    return prop_type if required else prop_type + "?"


def common_properties(union_name: str) -> list[tuple[str, str, str]]:
    union = unions[union_name]
    if union["primitive"] or not union["members"]:
        return []
    shared = None
    for member in union["members"]:
        props = {(p, property_type(t, f["required"]), j) for p, t, j, f in type_fields[member]}
        shared = props if shared is None else shared & props
    order = [p for p, _, _, _ in type_fields[union["members"][0]]]
    return sorted(shared or [], key=lambda item: order.index(item[0]))


# ─── Types file ───

out: list[str] = [
    "// <auto-generated>",
    f"//     Generated by scripts/codegen/generate_bot_api.py from Telegram Bot API {spec['version']}",
    f"//     ({spec['release_date']}). Do not edit by hand.",
    "// </auto-generated>",
    "#nullable enable",
    "using System.Text.Json;",
    "using System.Text.Json.Serialization;",
    "",
    "namespace ShiroBot.Telegram.Model.BotApi;",
    "",
    "/// <summary>Telegram Bot API version these types were generated from.</summary>",
    "public static class BotApiInfo",
    "{",
    f"    public const string Version = \"{spec['version'].replace('Bot API ', '')}\";",
    f"    public const string ReleaseDate = \"{spec['release_date']}\";",
    "",
    "    /// <summary>Every Update kind, for getUpdates/setWebhook allowed_updates.</summary>",
    "    public static IReadOnlyList<string> UpdateTypes { get; } =",
    "    [",
]
update_types = [f["name"] for f in types["Update"]["fields"] if f["name"] != "update_id"]
out += [f"        \"{u}\"," for u in update_types]
out += ["    ];", "}", ""]

for union_name, union in unions.items():
    out += doc(union["description"], "")
    if union["href"]:
        out.append(f"/// <remarks><see href=\"{union['href']}\"/></remarks>")
    out.append(f"[JsonConverter(typeof(BotApiUnionConverter<{union_name}>))]")
    props = common_properties(union_name)
    if props:
        out.append(f"public interface {union_name}")
        out.append("{")
        for prop, prop_type, _ in props:
            out.append(f"    {prop_type} {prop} {{ get; }}")
        out += ["}", ""]
    else:
        out += [f"public interface {union_name};", ""]
    # Unknown values keep every field and still expose the shared members.
    out.append(f"/// <summary>A {union_name} value this Bot API version does not know; all fields are kept.</summary>")
    if not props:
        out += [f"public sealed record {union_name}Unknown : BotApiObject, {union_name};", ""]
        continue
    out.append(f"public sealed record {union_name}Unknown : BotApiObject, {union_name}")
    out.append("{")
    for prop, prop_type, json_name in props:
        default = "" if prop_type.endswith("?") or prop_type in VALUE_TYPES else (
            " = [];" if prop_type.startswith("IReadOnlyList") else " = default!;")
        out.append(f"    [JsonPropertyName(\"{json_name}\")] public {prop_type} {prop} {{ get; init; }}{default}")
    out += ["}", ""]

for name, fields in type_fields.items():
    value = types[name]
    out += doc(value.get("description", []), "")
    out.append(f"/// <remarks><see href=\"{value['href']}\"/></remarks>")
    bases = ", ".join(["BotApiObject"] + implements.get(name, []))
    if not fields:
        out += [f"public sealed record {name} : {bases};", ""]
        continue
    out.append(f"public sealed record {name} : {bases}")
    out.append("{")
    disc = discriminator(name) if name in implements else None
    for index, (prop, prop_type, json_name, field) in enumerate(fields):
        if index:
            out.append("")
        out += doc(field.get("description", ""), "    ")
        out.append(f"    [JsonPropertyName(\"{json_name}\")]")
        if disc and disc[0] == json_name:
            out.append(f"    public string {prop} {{ get; init; }} = \"{disc[1]}\";")
        elif not field["required"]:
            out.append(f"    public {prop_type}? {prop} {{ get; init; }}")
        elif prop_type in VALUE_TYPES:
            out.append(f"    public {prop_type} {prop} {{ get; init; }}")
        elif prop_type.startswith("IReadOnlyList"):
            out.append(f"    public {prop_type} {prop} {{ get; init; }} = [];")
        else:
            out.append(f"    public {prop_type} {prop} {{ get; init; }} = default!;")
    out += ["}", ""]

# Union resolution.
out += ["internal static partial class BotApiUnionRegistry", "{",
        "    internal static Type Resolve(Type union, JsonElement element)", "    {"]
for union_name, union in unions.items():
    out.append(f"        if (union == typeof({union_name})) return Resolve{union_name}(element) ?? typeof({union_name}Unknown);")
out += ["        throw new NotSupportedException(\"Unknown Bot API union \" + union.Name + \".\");", "    }", ""]


def candidates(members: list[str], skip: str | None = None) -> str:
    parts = []
    for member in members:
        required = [f["name"] for f in types[member].get("fields", []) if f["required"] and f["name"] != skip]
        parts.append(f"(typeof({member}), [{', '.join(json.dumps(r) for r in required)}])")
    return "Pick(element, " + ", ".join(parts) + ")"


for union_name, union in unions.items():
    out.append(f"    private static Type? Resolve{union_name}(JsonElement element)")
    out.append("    {")
    if union_name == "MaybeInaccessibleMessage":
        out.append("        return IsZero(element, \"date\") ? typeof(InaccessibleMessage) : typeof(Message);")
        out += ["    }", ""]
        continue
    if union["primitive"]:
        out.append("        if (element.ValueKind == JsonValueKind.String) return typeof(RichTextPlain);")
        out.append("        if (element.ValueKind == JsonValueKind.Array) return typeof(RichTextList);")
    discs = {m: discriminator(m) for m in union["members"]}
    keys = {d[0] for d in discs.values() if d}
    if len(keys) == 1 and all(discs.values()):
        key = keys.pop()
        groups: dict[str, list[str]] = {}
        for member, (_, tag) in discs.items():
            groups.setdefault(tag, []).append(member)
        out.append(f"        return Text(element, \"{key}\") switch")
        out.append("        {")
        for tag, group in groups.items():
            target = f"typeof({group[0]})" if len(group) == 1 else candidates(group, key) + f" ?? typeof({group[0]})"
            out.append(f"            \"{tag}\" => {target},")
        out.append("            _ => null")
        out.append("        };")
    else:
        out.append(f"        return {candidates(union['members'])};")
    out += ["    }", ""]
out += ["}", ""]

TYPES_OUT.parent.mkdir(parents=True, exist_ok=True)
TYPES_OUT.write_text("\n".join(out), encoding="utf-8")

# ─── Methods file ───


def return_type(returns: list[str]) -> tuple[str, str]:
    if returns == ["Message", "Boolean"]:
        return "Api.Message?", "InvokeMessageOrTrueAsync"
    if len(returns) != 1:
        raise SystemExit("Unsupported return types: " + ", ".join(returns))
    return map_type(returns, "", None, "Api."), "InvokeAsync"


iface: list[str] = []
impl: list[str] = []
for name, value in methods.items():
    params = method_params[name]
    ordered = [p for p in params if p[2]["required"]] + [p for p in params if not p[2]["required"]]
    result, invoker = return_type(value["returns"])
    signature = []
    for param, param_type, field in ordered:
        signature.append(f"{param_type} {param}" if field["required"] else f"{param_type}? {param} = null")
    signature.append("CancellationToken cancellationToken = default")
    method_name = pascal(name) + "Async"
    iface += doc(value.get("description", []), "    ")
    for param, _, field in ordered:
        iface.append(f"    /// <param name=\"{param.lstrip('@')}\">{xml(field['description'])}</param>")
    iface.append(f"    /// <remarks><see href=\"{value['href']}\"/></remarks>")
    iface.append(f"    Task<{result}> {method_name}({', '.join(signature)});")
    iface.append("")
    impl.append("    /// <inheritdoc/>")
    impl.append(f"    public Task<{result}> {method_name}({', '.join(signature)}) =>")
    generic = "" if invoker == "InvokeMessageOrTrueAsync" else f"<{result}>"
    args = ", ".join(f"new(\"{field['name']}\", {param})" for param, _, field in params)
    impl.append(f"        {invoker}{generic}(\"{name}\", [{args}], cancellationToken);")
    impl.append("")

methods_out = [
    "// <auto-generated>",
    f"//     Generated by scripts/codegen/generate_bot_api.py from Telegram Bot API {spec['version']}",
    f"//     ({spec['release_date']}). Do not edit by hand.",
    "// </auto-generated>",
    "#nullable enable",
    "using Api = ShiroBot.Telegram.Model.BotApi;",
    "",
    "namespace ShiroBot.TelegramAdapter.BotApi;",
    "",
    "public partial interface ITelegramBotApi",
    "{",
    *iface,
    "}",
    "",
    "public sealed partial class TelegramBotApi",
    "{",
    *impl,
    "}",
    "",
]
METHODS_OUT.parent.mkdir(parents=True, exist_ok=True)
METHODS_OUT.write_text("\n".join(methods_out), encoding="utf-8")
print(f"{spec['version']}: {len(type_fields)} types, {len(unions)} unions, {len(methods)} methods", file=sys.stderr)
