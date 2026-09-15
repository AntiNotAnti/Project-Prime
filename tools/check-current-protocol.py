#!/usr/bin/env python3
"""Verify that the live protocol document follows ``NetHeader.Version``.

The current version is deliberately allowed to be expressed through aliases in
the C# source (for example ``Version = EnhancedHuntersVersion``).  This guard
evaluates the small, side-effect-free constant expression language used by the
header instead of looking for a numeric literal beside ``Version``.  Historical
protocol documents are not inspected; only the explicitly marked current
reference is a live contract.
"""
from __future__ import annotations

import argparse
import ast
import re
import sys
from pathlib import Path
from typing import Mapping


SOURCE = Path("src/Game/Protocol/NetHeader.cs")
DOCUMENT = Path("docs/CURRENT_PROTOCOL.md")

# C# integer literals may carry one or more of these suffixes.  The expression
# evaluator below intentionally does not support arbitrary C#; a new expression
# form should be added here only when it remains safe and deterministic.
_INTEGER_SUFFIXES = re.compile(r"(?i)(?<=\d)[ulfdm]+\b")
_CONSTANT = re.compile(
    r"\bconst\s+(?:byte|sbyte|short|ushort|int|uint|long|ulong)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<value>[^;]+);"
)
_CURRENT_STATUS = re.compile(
    r"(?ims)^[ \t]*Status:.*?\bcurrent\s+authoritative\s+wire\s+family\b"
    r".*?\bprotocol\s+\*{0,2}(?P<version>\d+)\b"
)


class ProtocolExpressionError(ValueError):
    """Raised when a protocol constant is not a supported constant expression."""


def _strip_comments(source: str) -> str:
    """Remove comments while retaining line structure for useful diagnostics."""
    source = re.sub(r"/\*.*?\*/", lambda match: "\n" * match.group().count("\n"),
                    source, flags=re.DOTALL)
    return re.sub(r"//[^\n]*", "", source)


def _normalize_integer_literals(expression: str) -> str:
    # Python accepts C#-style hexadecimal and binary literals, but not the
    # suffixes.  Decimal literals with a leading zero are not valid modern C#
    # constants either, so no octal compatibility is necessary here.
    return _INTEGER_SUFFIXES.sub("", expression)


def _evaluate(node: ast.AST, constants: Mapping[str, str], resolving: set[str]) -> int:
    if isinstance(node, ast.Constant) and isinstance(node.value, int) \
            and not isinstance(node.value, bool):
        return node.value
    if isinstance(node, ast.Name):
        if node.id in resolving:
            raise ProtocolExpressionError(f"cyclic protocol constant alias: {node.id}")
        value = constants.get(node.id)
        if value is None:
            raise ProtocolExpressionError(f"unknown protocol constant: {node.id}")
        resolving.add(node.id)
        try:
            parsed = ast.parse(_normalize_integer_literals(value), mode="eval").body
            return _evaluate(parsed, constants, resolving)
        finally:
            resolving.remove(node.id)
    if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub, ast.Invert)):
        value = _evaluate(node.operand, constants, resolving)
        if isinstance(node.op, ast.UAdd):
            return value
        if isinstance(node.op, ast.USub):
            return -value
        return ~value
    if isinstance(node, ast.BinOp) and isinstance(
            node.op, (ast.Add, ast.Sub, ast.Mult, ast.BitOr, ast.BitAnd,
                      ast.BitXor, ast.LShift, ast.RShift)):
        left = _evaluate(node.left, constants, resolving)
        right = _evaluate(node.right, constants, resolving)
        if isinstance(node.op, ast.Add):
            return left + right
        if isinstance(node.op, ast.Sub):
            return left - right
        if isinstance(node.op, ast.Mult):
            return left * right
        if isinstance(node.op, ast.BitOr):
            return left | right
        if isinstance(node.op, ast.BitAnd):
            return left & right
        if isinstance(node.op, ast.BitXor):
            return left ^ right
        if isinstance(node.op, ast.LShift):
            return left << right
        return left >> right
    raise ProtocolExpressionError(
        f"unsupported protocol constant expression: {ast.unparse(node)}")


def resolve_source_protocol(source: str) -> int:
    """Resolve the ``Version`` constant, including aliases, from C# source."""
    constants = {
        match.group("name"): match.group("value").strip()
        for match in _CONSTANT.finditer(_strip_comments(source))
    }
    expression = constants.get("Version")
    if expression is None:
        raise ProtocolExpressionError("NetHeader.Version constant was not found")
    parsed = ast.parse(_normalize_integer_literals(expression), mode="eval").body
    return _evaluate(parsed, constants, {"Version"})


def read_documented_protocol(document: str) -> int:
    """Read only the live-current protocol claim from the status paragraph."""
    matches = list(_CURRENT_STATUS.finditer(document))
    if len(matches) != 1:
        raise ValueError(
            "CURRENT_PROTOCOL.md must contain exactly one current authoritative "
            "protocol status claim")
    return int(matches[0].group("version"))


def check(root: Path) -> list[str]:
    """Return human-readable violations for ``root``."""
    source_path = root / SOURCE
    document_path = root / DOCUMENT
    errors: list[str] = []
    try:
        source_protocol = resolve_source_protocol(source_path.read_text(encoding="utf-8"))
    except (OSError, SyntaxError, ProtocolExpressionError) as error:
        errors.append(f"{source_path.relative_to(root)}: {error}")
        return errors
    try:
        documented_protocol = read_documented_protocol(
            document_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        errors.append(f"{document_path.relative_to(root)}: {error}")
        return errors
    if source_protocol != documented_protocol:
        errors.append(
            f"{document_path.relative_to(root)}: live protocol is {documented_protocol}, "
            f"but {source_path.relative_to(root)} resolves NetHeader.Version to "
            f"{source_protocol}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path,
                        default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.root.resolve()
    try:
        errors = check(root)
    except OSError as error:
        print(f"current protocol: {error}", file=sys.stderr)
        return 2
    for error in errors:
        print(error)
    if errors:
        return 1
    print(f"current protocol: NetHeader.Version matches {DOCUMENT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
