"""Robinhood MCP server client using stdio transport (JSON-RPC 2.0 over subprocess pipes)."""

from __future__ import annotations

import asyncio
import json
import logging
import os
from typing import Any

logger = logging.getLogger(__name__)


class MCPToolError(Exception):
    pass


class RobinhoodMCPClient:
    def __init__(self, command: list[str], env_vars: dict[str, str]) -> None:
        self._command = command
        self._env_vars = env_vars
        self._process: asyncio.subprocess.Process | None = None
        self._request_id = 0
        self._lock = asyncio.Lock()

    async def start(self) -> None:
        env = {**os.environ}
        for key, value in self._env_vars.items():
            resolved = os.path.expandvars(value)
            if resolved != value or not value.startswith("${"):
                env[key] = resolved
            elif key in os.environ:
                env[key] = os.environ[key]
        self._process = await asyncio.create_subprocess_exec(
            *self._command,
            stdin=asyncio.subprocess.PIPE,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            env=env,
        )
        logger.info("MCP server process started (pid=%s)", self._process.pid)
        await self._send_request("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "trading-agent", "version": "1.0"},
        })
        await self._send_notification("notifications/initialized", {})
        logger.info("MCP handshake complete")

    async def stop(self) -> None:
        if self._process and self._process.returncode is None:
            self._process.terminate()
            try:
                await asyncio.wait_for(self._process.wait(), timeout=5.0)
            except asyncio.TimeoutError:
                self._process.kill()
            logger.info("MCP server process stopped")

    async def call_tool(self, tool_name: str, arguments: dict[str, Any]) -> Any:
        result = await self._send_request("tools/call", {"name": tool_name, "arguments": arguments})
        if "error" in result:
            raise MCPToolError(f"{tool_name}: {result['error']}")
        content = result.get("result", {}).get("content", [])
        if isinstance(content, list) and content:
            first = content[0]
            if isinstance(first, dict) and first.get("type") == "text":
                try:
                    return json.loads(first["text"])
                except (json.JSONDecodeError, KeyError):
                    return first.get("text", content)
        return result.get("result", result)

    async def _send_request(self, method: str, params: dict[str, Any]) -> dict[str, Any]:
        async with self._lock:
            self._request_id += 1
            req_id = self._request_id
            message = {"jsonrpc": "2.0", "id": req_id, "method": method, "params": params}
            assert self._process and self._process.stdin
            self._process.stdin.write((json.dumps(message) + "\n").encode())
            await self._process.stdin.drain()
            return await asyncio.wait_for(self._read_response(req_id), timeout=30.0)

    async def _send_notification(self, method: str, params: dict[str, Any]) -> None:
        async with self._lock:
            message = {"jsonrpc": "2.0", "method": method, "params": params}
            assert self._process and self._process.stdin
            self._process.stdin.write((json.dumps(message) + "\n").encode())
            await self._process.stdin.drain()

    async def _read_response(self, request_id: int) -> dict[str, Any]:
        assert self._process and self._process.stdout
        while True:
            line = await self._process.stdout.readline()
            if not line:
                raise MCPToolError("MCP server closed stdout unexpectedly")
            try:
                msg = json.loads(line.decode().strip())
            except json.JSONDecodeError:
                continue
            if msg.get("id") == request_id:
                return msg

    async def get_portfolio(self) -> dict: return await self.call_tool("get_portfolio", {})
    async def get_equity_quotes(self, symbols: list[str]) -> dict: return await self.call_tool("get_equity_quotes", {"symbols": symbols})
    async def get_equity_historicals(self, symbol: str, interval: str, span: str) -> dict: return await self.call_tool("get_equity_historicals", {"symbol": symbol, "interval": interval, "span": span})
    async def get_equity_fundamentals(self, symbols: list[str]) -> dict: return await self.call_tool("get_equity_fundamentals", {"symbols": symbols})
    async def get_equity_positions(self) -> dict: return await self.call_tool("get_equity_positions", {})
    async def get_equity_orders(self, state: str = "open") -> dict: return await self.call_tool("get_equity_orders", {"state": state})
    async def get_watchlist_items(self, watchlist_name: str) -> dict: return await self.call_tool("get_watchlist_items", {"watchlist_name": watchlist_name})
    async def get_earnings_calendar(self) -> dict: return await self.call_tool("get_earnings_calendar", {})
    async def get_earnings_results(self, symbol: str) -> dict: return await self.call_tool("get_earnings_results", {"symbol": symbol})
    async def get_index_quotes(self, symbols: list[str]) -> dict: return await self.call_tool("get_index_quotes", {"symbols": symbols})
    async def place_equity_order(self, **kwargs: Any) -> dict: return await self.call_tool("place_equity_order", kwargs)
    async def review_equity_order(self, **kwargs: Any) -> dict: return await self.call_tool("review_equity_order", kwargs)
    async def cancel_equity_order(self, order_id: str) -> dict: return await self.call_tool("cancel_equity_order", {"order_id": order_id})
