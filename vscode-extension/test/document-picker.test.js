'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { openDocumentPicker, renderDocumentPicker } = require('../lib/document-picker');
const documents = [{ role: 'business', title: 'Business requirements', candidates: [{ path: 'docs/brd.md', title: 'Existing BRD',
  sizeBytes: 12345, lastModifiedUtc: '2026-09-27T10:00:00Z', summary: 'The product serves customers. <script>bad()</script>' }] }];

test('eligible file list has metadata, expandable summaries and adjacent selection actions', () => {
  const html = renderDocumentPicker({ cspSource: 'test:' }, documents, 'business', [], 'nonce');
  for (const value of ['docs/brd.md', '12,345 bytes', '2026-09-27T10:00:00Z', '<details><summary>Document summary</summary>', 'Select file', 'Load document manually']) assert.ok(html.includes(value));
  assert.ok(html.includes('&lt;script&gt;bad()&lt;/script&gt;'));
  assert.ok(!html.includes('<script>bad()'));
  assert.match(html, /default-src 'none'/u);
});

test('picker selects only a listed file and settles once, including on close', async () => {
  let receive, close; let disposed = 0;
  const vscode = { ViewColumn: { Active: 1 }, window: { createWebviewPanel: () => ({
    onDidDispose: handler => { close = handler; }, dispose: () => { disposed++; close(); },
    webview: { cspSource: 'test:', onDidReceiveMessage: handler => { receive = handler; } },
  }) } };
  const result = openDocumentPicker(vscode, documents, 'business');
  receive({ command: 'select-file', value: '../../other.md' });
  receive({ command: 'select-file', value: '100' });
  assert.equal(disposed, 0);
  receive({ command: 'select-file', value: '0' });
  receive({ command: 'select-file', value: '0' });
  assert.deepEqual(await result, { role: documents[0], relative: 'docs/brd.md' });
  assert.equal(disposed, 1);
  const cancelled = openDocumentPicker(vscode, documents, 'business'); close();
  assert.equal(await cancelled, undefined);
});
