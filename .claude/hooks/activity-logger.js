#!/usr/bin/env node
'use strict';

const fs = require('fs');
const path = require('path');

let rawData = '';
try {
  rawData = fs.readFileSync(0, 'utf8');
} catch {
  process.exit(0);
}

let data;
try {
  data = JSON.parse(rawData);
} catch {
  process.exit(0);
}

const event = data.hook_event_name || '';
const tool = data.tool_name || '';
const input = data.tool_input || {};
const ts = new Date().toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' });

let line = null;

switch (event) {
  case 'UserPromptSubmit': {
    const prompt = String(data.prompt || '').replace(/\r?\n/g, ' ').substring(0, 120);
    line = `${ts} [PROMPT]       "${prompt}"`;
    break;
  }

  case 'PostToolUse': {
    switch (tool) {
      case 'Read':
        line = `${ts} [READ]         ${input.file_path || ''}`;
        break;
      case 'Write':
        line = `${ts} [WRITE]        ${input.file_path || ''}`;
        break;
      case 'Edit':
        line = `${ts} [EDIT]         ${input.file_path || ''}`;
        break;
      case 'Bash':
        line = `${ts} [BASH]         ${String(input.command || '').replace(/\r?\n/g, ' ').substring(0, 100)}`;
        break;
      case 'Grep':
        line = `${ts} [GREP]         pattern="${String(input.pattern || '').substring(0, 50)}" in="${input.path || '.'}"`;
        break;
      case 'Glob':
        line = `${ts} [GLOB]         ${input.pattern || ''}`;
        break;
      default:
        line = `${ts} [TOOL]         [${tool}]`;
    }
    break;
  }

  case 'InstructionsLoaded': {
    // file_path or source_file depending on Claude Code version
    const filePath = data.file_path || data.source_file || data.path || '';
    const reason = data.source || data.load_reason || data.matcher || '';
    const fileName = filePath ? path.basename(filePath) : '(unknown)';
    line = `${ts} [INSTRUCTIONS] Loaded: ${fileName}${reason ? ' (' + reason + ')' : ''}`;
    break;
  }

  case 'SessionStart': {
    const source = data.source || 'startup';
    line = `${ts} [SESSION]      Started (${source})`;
    break;
  }

  case 'Stop': {
    line = `${ts} [STOP]         Turn complete`;
    break;
  }

  default:
    process.exit(0);
}

if (!line) process.exit(0);

try {
  const logPath = path.join('.claude', 'activity.log');
  fs.appendFileSync(logPath, line + '\n', 'utf8');
} catch {
  // Silently fail — never block Claude due to logging errors
}

process.exit(0);
