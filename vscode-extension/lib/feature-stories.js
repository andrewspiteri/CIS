'use strict';

const { escapeHtml: h } = require('./security');
const { renderReviewText } = require('./feature-review-text');

function isStoryField(field) {
  return ['delivery-stories-foundation', 'delivery-stories-mvp', 'delivery-stories-post-mvp'].includes(field.id);
}

function renderStoryList(value) {
  const text = String(value || '').replace(/<!--[^]*?-->/gu, '');
  const headings = [...text.matchAll(/^###\s+(.+)$/gmu)];
  if (!headings.length) return `<div class="feature-review-text">${renderReviewText(text)}</div>`;
  const intro = text.slice(0, headings[0].index).trim();
  return `${intro ? `<div class="feature-review-text">${renderReviewText(intro)}</div>` : ''}<p class="muted">${headings.length} user ${headings.length === 1 ? 'story' : 'stories'} · Open a story to review its acceptance outline.</p><ol class="feature-story-list">${headings.map((match, index) => {
    const body = text.slice(match.index + match[0].length, headings[index + 1]?.index ?? text.length);
    return `<li><details class="feature-story"><summary>${h(match[1])}</summary><div class="feature-review-text">${renderReviewText(body)}</div></details></li>`;
  }).join('')}</ol>`;
}

const STORY_STYLES = `.feature-story-list{padding-left:1.5rem}.feature-story-list>li{margin:.6rem 0}.feature-story{padding:.5rem;border:1px solid var(--vscode-panel-border);border-radius:.25rem}.feature-story>summary{cursor:pointer;font-weight:600}.feature-story-group{margin-bottom:1.2rem}.feature-story-group>h3{font-size:1.15rem}.feature-story-group .feature-answer-editor textarea{min-height:20rem}`;

module.exports = { isStoryField, renderStoryList, STORY_STYLES };
