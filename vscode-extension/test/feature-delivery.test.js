'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { deliveryAssessment, reconciledDrafts } = require('../lib/feature-delivery');

test('uncertainty explains the search limit and offers a prefilled planning decision without claiming absence', () => {
  const html = deliveryAssessment({status:'missing',featureRepositoryId:'referrals',repositoryIds:['referrals'],stories:[{
    id:'privacy',title:'Privacy evidence',treatment:'unresolved',assessmentState:'no-matching-evidence',assessmentReason:'No matching source found; absence is not proven.',
    requirements:['Retain document version and acknowledgement timestamp.'],owners:[],evidenceIds:[]}],evidence:[]});
  assert.match(html,/No matching code found/u); assert.doesNotMatch(html,/Needs investigation/u);
  assert.match(html,/Save story decision/u); assert.match(html,/value="" selected/u);
  assert.doesNotMatch(html,/value="referrals" checked/u); assert.match(html,/Retain document version/u);
  assert.match(html,/data-delivery-field="plan"/u); assert.doesNotMatch(html,/data-delivery-field="reason"/u);
});

test('related code does not choose extension and requirement checks distinguish leads from established coverage', () => {
  const html = deliveryAssessment({status:'current',repositoryIds:['customer-ui','referrals'],featureRepositoryId:'referrals',stories:[{
    id:'clicks',title:'Click Tracking',treatment:'unresolved',assessmentState:'inconclusive',owners:[],evidenceIds:['E1'],
    requirements:['Record product selection.','Retain clicks for ten years.'],requirementChecks:[
      {number:1,requirement:'Record product selection.',evidenceIds:['E1']},{number:2,requirement:'Retain clicks for ten years.',evidenceIds:[]}]
  }],evidence:[{id:'E1',repositoryId:'customer-ui',path:'products.ts',kind:'integration-point',summary:'selectProduct is a possible integration point.',excerpt:'selectProduct() { navigate(); }'}]});
  assert.match(html,/value="" selected/u); assert.doesNotMatch(html,/value="extend" selected/u);
  assert.match(html,/Requirement checks \(2\)/u); assert.match(html,/No supporting code established/u);
  assert.match(html,/Possible integration point/u); assert.match(html,/selectProduct is a possible integration point/u);
  assert.match(html,/<details><summary>View code excerpt<\/summary><pre>/u);
  assert.doesNotMatch(html,/<summary>Implementation evidence<\/summary>/u);
});

test('saved choices and stale evidence remain distinguishable from automated findings', () => {
  const story={id:'privacy',title:'Privacy evidence',treatment:'unresolved',assessmentState:'inconclusive',requirements:[],owners:[],evidenceIds:[],
    review:{treatment:'new',plan:'Build acknowledgement recording.',owners:['referrals'],actor:'Andrew',evidenceHashes:{}},reviewCurrent:true};
  assert.match(deliveryAssessment({status:'current',stories:[story],repositoryIds:['referrals']}),/New work · Decision saved/u);
  assert.match(deliveryAssessment({status:'stale',stories:[{...story,reviewCurrent:false}],repositoryIds:['referrals']}),/Saved decision needs recheck/u);
});

test('new work is explicitly a proposal and prefills its proposed owner without unrelated code references', () => {
  const html=deliveryAssessment({status:'current',featureRepositoryId:'referrals',repositoryIds:['referrals','customer-ui'],stories:[{
    id:'clicks',title:'Click Tracking',treatment:'new',assessmentState:'proposed',existingCapability:'unknown',remainingWork:'Add recording.',owners:['customer-ui'],evidenceIds:['E1']
  }],evidence:[{id:'E1',repositoryId:'customer-ui',path:'products.ts',excerpt:'selectProduct() {}'}]});
  assert.match(html,/New work · Proposal/u); assert.match(html,/Proposed work to review/u);
  assert.match(html,/Not established from the selected code/u);
  assert.match(html,/value="customer-ui" checked/u); assert.doesNotMatch(html,/value="referrals" checked/u);
  assert.match(html,/<textarea data-delivery-field="evidencePaths"[^>]*><\/textarea>/u);
});

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
