'use strict';

const { escapeHtml: h } = require('./security');
const { renderReviewText } = require('./feature-review-text');
const { createHash } = require('node:crypto');

const MVP = 'delivery-stories-mvp';
const LATER = 'delivery-stories-post-mvp';
const EMPTY = {
  [MVP]: 'No MVP stories are currently selected.',
  [LATER]: 'No Post-MVP stories are currently selected.',
};
const NO_LATER_SUGGESTION = 'No Post-MVP stories are established by the current BRD. Review and confirm that no later delivery is required.';

function isStoryField(field) {
  return ['delivery-stories-foundation', 'delivery-stories-mvp', 'delivery-stories-post-mvp'].includes(field.id);
}

function parseStories(value) {
  const text = String(value || '').replace(/\r\n/gu, '\n');
  // Mask comments for boundary detection, but move the original Markdown. Code
  // examples and hidden provenance must never become separate story cards.
  const visible = text.replace(/<!--[^]*?-->/gu, comment => comment.replace(/[^\n]/gu, ' '));
  const headings = []; let offset = 0; let fence;
  for (const line of visible.split('\n')) {
    const marker = /^\s*(`{3,}|~{3,})/u.exec(line);
    if (marker) {
      if (!fence) fence = marker[1];
      else if (marker[1].startsWith(fence)) fence = undefined;
    } else if (!fence) {
      const match = /^###[ \t]+(.+)$/u.exec(line);
      if (match) headings.push({ title: match[1].trim(), start: offset, body: offset + line.length });
    }
    offset += line.length + 1;
  }
  return { intro: text.slice(0, headings[0]?.start ?? text.length).trim(), stories: headings.map((heading, index) => {
    const end = headings[index + 1]?.start ?? text.length;
    const block = text.slice(heading.start, end).trim();
    return { title: heading.title, body: text.slice(heading.body, end).trim(), block,
      key: createHash('sha256').update(block).digest('hex') };
  }) };
}

function moveStory(answers, request) {
  const from = request?.fieldId;
  if (![MVP, LATER].includes(from) || !Number.isSafeInteger(request.index) || request.index < 0 || typeof request.key !== 'string')
    throw new Error('Select an MVP or Post-MVP story to move.');
  const to = from === MVP ? LATER : MVP;
  const source = parseStories(answers[from]); const destination = parseStories(answers[to]);
  const story = source.stories[request.index];
  if (!story || story.key !== request.key)
    throw new Error('This story changed since its card was displayed. Your edits are retained. Review the updated card and try the move again.');
  const join = (intro, stories) => [intro, ...stories.map(item => item.block)].filter(Boolean).join('\n\n');
  const remaining = source.stories.filter((_, index) => index !== request.index);
  const intro = [EMPTY[to], NO_LATER_SUGGESTION].includes(destination.intro) ? '' : destination.intro;
  const fields = {
    [from]: join(source.intro, remaining) || EMPTY[from],
    [to]: join(intro, [...destination.stories, story]),
  };
  if (Object.values(fields).some(text => text.length > 24_000))
    throw new Error('The destination list would exceed the answer limit. Shorten it before moving this story; both lists are unchanged.');
  return { fields, title: story.title, category: to === MVP ? 'MVP' : 'Post-MVP', focus: { fieldId: to, index: destination.stories.length } };
}

function renderStoryList(value, { fieldId, busy = false, focusIndex } = {}) {
  const { intro, stories } = parseStories(value);
  if (!stories.length) return `<div class="feature-review-text">${renderReviewText(value)}</div>`;
  const action = fieldId === MVP ? 'Move to Post-MVP' : fieldId === LATER ? 'Promote to MVP' : undefined;
  return `${intro ? `<div class="feature-review-text">${renderReviewText(intro)}</div>` : ''}<p class="muted">${stories.length} user ${stories.length === 1 ? 'story' : 'stories'} · Open a story to review its acceptance outline.</p><ol class="feature-story-list">${stories.map((story, index) => {
    const target = JSON.stringify({ fieldId, index, key: story.key });
    return `<li ${focusIndex === index ? 'data-story-focus' : ''}><div class="feature-story-row"><details class="feature-story"><summary>${h(story.title)}</summary><div class="feature-review-text">${renderReviewText(story.body)}</div></details>${action ? `<button type="button" class="secondary" data-wizard-action="move-story" data-value="${h(target)}" aria-label="${h(action + ': ' + story.title)}" ${busy ? 'disabled' : ''}>${action}</button>` : ''}</div></li>`;
  }).join('')}</ol>`;
}

const STORY_STYLES = `.feature-story-list{padding-left:1.5rem}.feature-story-list>li{margin:.6rem 0}.feature-story-row{display:flex;align-items:flex-start;gap:.65rem}.feature-story-row>.feature-story{flex:1;min-width:0}.feature-story-row>button{flex-shrink:0}.feature-story{padding:.5rem;border:1px solid var(--vscode-panel-border);border-radius:.25rem}.feature-story>summary{cursor:pointer;font-weight:600}.feature-story-group{margin-bottom:1.2rem}.feature-story-group>h3{font-size:1.15rem}.feature-story-group .feature-answer-editor textarea{min-height:20rem}@media(max-width:800px){.feature-story-row{flex-direction:column}.feature-story-row>.feature-story{box-sizing:border-box;width:100%}}`;

module.exports = { isStoryField, parseStories, moveStory, renderStoryList, STORY_STYLES };
