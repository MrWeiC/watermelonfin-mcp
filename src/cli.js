#!/usr/bin/env node
import {StdioServerTransport} from '@modelcontextprotocol/sdk/server/stdio.js';
import {createMcpServer} from './server.js';
try{await createMcpServer().connect(new StdioServerTransport());}catch{console.error('WatermelonFin MCP could not start. Check local configuration.');process.exitCode=1;}
