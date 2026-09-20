'use strict';
const assert = require('node:assert/strict');
const test = require('node:test');
const { gallery, exportScreen } = require('../lib/feature-screens');
const result = { status: 'current', inputHash: 'source', warnings: [], screens: [{ baselineRepository: 'frontend', svg: '<svg/>',
  plan: { id: 'capture', title: 'Contact <script>capture</script>', purpose: 'Capture contact details', frontendType: 'public', route: '/capture',
    actions: [{ label: 'Continue', destination: 'Redirect immediately' }], states: ['Invalid email'], evidence: 'Capture form' } }] };

test('gallery displays images, full-size links, changes and states with escaped source content', () => {
  const html = gallery(result, { dirty: true });
  assert.match(html, /data:image\/svg\+xml;base64,/);
  assert.match(html, /open-feature-screen/); assert.match(html, /Save JPG/);
  assert.match(html, /unsaved experience answers/); assert.match(html, /Redirect immediately/);
  assert.match(html, /&lt;script&gt;/); assert.doesNotMatch(html, /<script>/);
  assert.doesNotMatch(gallery(result, { standalone: true }), /data-wizard-action/);
});

test('questionnaire drafts do not block screen feedback, while actual stale previews explain the recovery action', () => {
  const button = html => html.match(/<button[^>]*data-screen-decision="amend"[^>]*>/)[0];
  const current = gallery(result, { dirty: true, drafts: { key: 'Remove the optional field.' } });
  assert.doesNotMatch(button(current), /disabled/);
  assert.match(current, /feedback applies to the displayed previews using the saved direction/);
  assert.match(button(gallery(result, { busy: true })), /disabled/);
  const stale = gallery({ ...result, status: 'stale' }, { dirty: true });
  assert.match(button(stale), /disabled/);
  assert.match(stale, /This preview is out of date/);
  assert.match(stale, /data-wizard-action="generate-screens"[^>]*>Save answers and regenerate screens/);
});

test('exports only checked JPG payloads bound to a gallery image', () => {
  const value = JSON.stringify({ index: 0, sourceHash: 'source', data: 'data:image/jpeg;base64,/9j/2Q==' });
  assert.equal(exportScreen(result, value).filename, 'capture-proposed-screen.jpg');
  assert.throws(() => exportScreen(result, value.replace('source', 'stale')), /changed/);
  assert.throws(() => exportScreen(result, value.replace('"index":0', '"index":9')), /changed/);
  assert.throws(() => exportScreen(result, value.replace('image/jpeg', 'text/html')), /Only a rendered JPG/);
});

test('rejected screens collapse with restore controls and amendments retain their feedback', () => {
  const screen = { ...result.screens[0], key: 'key', revision: 'pixels' };
  const review = { id: 'review', key: 'key', actor: 'Reviewer', decision: 'not-needed', feedback: 'The host already does this.' };
  const rejected = gallery({ ...result, screens: [screen], reviews: [review] });
  assert.match(rejected, /<details class="feature-screen/);
  assert.match(rejected, /Not needed: Contact/);
  assert.match(rejected, /Include this screen again/);
  assert.match(rejected, /The host already does this/);
  const pending = gallery({ ...result, screens: [screen], reviews: [{ ...review, decision: 'amend' }] }, { drafts: { key: 'Remove the mobile field.' } });
  assert.match(pending, /Changes requested/);
  assert.match(pending, /Remove the mobile field/);
  assert.match(pending, /Apply changes to this screen/);
  assert.doesNotMatch(gallery({ ...result, screens: [screen], reviews: [review] }, { standalone: true }), /data-screen-decision/);
});
