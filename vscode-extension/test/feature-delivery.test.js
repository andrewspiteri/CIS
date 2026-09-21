'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { deliveryAssessment, reconciledDrafts } = require('../lib/feature-delivery');

test('delivery distinguishes requirements from implementation and exposes reconciliation', () => {
  const html = deliveryAssessment(undefined);
  assert.match(html, /Reconcile with existing implementation/u);
  assert.match(html, /not established which capabilities need building/u);
  assert.doesNotMatch(html, /Use reconciled stories as draft/u);
  assert.match(deliveryAssessment({status:'stale'}), /changed/u);
});

test('delivery proposals render safe evidence, owners and conflicts without declaring completion', () => {
  const html = deliveryAssessment({status:'current', stories:[{title:'Product catalogue',treatment:'conflict',existingCapability:'<script>bad()</script>',remainingWork:'Add adapter',owners:['backend'],evidenceIds:['E1'],conflict:'Source ownership differs'}],evidence:[{id:'E1',repositoryId:'backend',path:'products.ts',excerpt:'createProduct()'}],suggestedAnswers:{'delivery-stories-mvp':'draft'}}, {busy:true});
  assert.match(html,/Scope conflict/u); assert.match(html,/backend/u); assert.match(html,/createProduct/u);
  assert.doesNotMatch(html,/<script>/u); assert.match(html,/data-wizard-action="reconcile-delivery" disabled/u);
});

test('using reconciliation drafts preserves other answers and rejects stale results', () => {
  const model={pageDrafts:{delivery:{'delivery-ownership':'Confirmed upstream owner','delivery-stories-mvp':'previous draft'}},featureDelivery:{status:'current',suggestedAnswers:{'delivery-stories-mvp':'new draft',unexpected:'ignore'}}};
  assert.deepEqual(reconciledDrafts(model),{'delivery-ownership':'Confirmed upstream owner','delivery-stories-mvp':'new draft'});
  assert.equal(model.pageDrafts.delivery['delivery-stories-mvp'],'previous draft');
  model.featureDelivery.status='stale';assert.throws(()=>reconciledDrafts(model),/Reconcile current/u);
});
