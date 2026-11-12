import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
const root=path.resolve(process.env.RIGMD_UI_EVIDENCE || 'artifacts/startup-feedback'), build=path.resolve(process.env.RIGMD_UI_BUILD || '../backend-dotnet/RigMD.Api/wwwroot'); fs.mkdirSync(root,{recursive:true});
const {chromium}=await import(process.env.RIGMD_PLAYWRIGHT_MODULE ? pathToFileURL(path.resolve(process.env.RIGMD_PLAYWRIGHT_MODULE)).href : 'playwright');
const server=http.createServer((req,res)=>{const requested=path.resolve(build,'.'+new URL(req.url,'http://localhost').pathname);const file=requested.startsWith(build+path.sep)&&fs.existsSync(requested)&&fs.statSync(requested).isFile()?requested:path.join(build,'index.html');res.setHeader('Content-Type',({'.html':'text/html','.js':'application/javascript','.css':'text/css','.svg':'image/svg+xml'})[path.extname(file)]||'application/octet-stream');fs.createReadStream(file).pipe(res);});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const origin=`http://localhost:${server.address().port}`;
const token='A'.repeat(64), checks=[],errors=[];
checks.push = function(...items) { for (const item of items) console.log('PASS:',item); return Array.prototype.push.apply(this,items); };
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const summary={server_time:new Date().toISOString(),totals:{total_sessions:0,this_month_count:0,escalated_count:0},last_diagnosis:null,current_action_status:null,recurring_issues_count:0,warning_signs_active_count:0,action_distribution:['Monitor','Maintain','Troubleshoot','Escalate'].map(label=>({label,count:0})),session_frequency:[],recent_warning_signs:[],last_saved_session:null};
const full={device_name:'TEST-PC',device_type:'Desktop',os_version:'Test Windows',system_age:'Not available',chipset_driver:'Not available',storage_type:'SSD',active_power_plan:'Balanced',connected_displays:null,displays:[],device_errors:[],battery:null,network:null,cpu:{name:'Fixture full-inventory CPU',usage_percent:25,usage_source:'Test fixture',cores:4,threads:8,frequency_mhz:2000,temperature_celsius:null},gpu:{name:'Fixture GPU',driver:'Test driver',type:'Integrated',vram_gb:0},ram:{total_gb:16,used_gb:4,usage_percent:25,slots_used:null,slots_total:null},disk:{total_gb:100,used_gb:40,usage_percent:40},storage_drives:[],all_disks:[]};
const warningRows=[{id:'WS-006',warning_sign:'Storage SMART Caution',meaning:'Saved failing storage status.',threshold:'Recorded warning',action:'Escalate',category:'Storage',keywords:['smart'],observed:true,observed_count:2},{id:'WS-007',warning_sign:'Heat',meaning:'Thermal reference',threshold:'Recorded heat',action:'Maintain',category:'Thermal',keywords:['heat'],observed:false,observed_count:0}];
let cpuValue=0,memoryValue=0,spaceValue=40,readingsAvailable=true,hasIp=true,linksMode='ready',releaseLinks;
const openedSessionIds=[];
let homes=0,inventories=0,sensors=0,inventoryMode='hold',releaseInventory,releaseSessions,releaseAlerts;
let sessionsHeld=false,alertsMode='hold',warningRequests=0,summaryMode='ready';
let homeHeld=true,releaseHome,homeMode='ready',recheckMode='fail',recheckCount=0,feedbackSaves=0;const feedbackBySession=new Map(); let recoveryBlocked=false; const loadedAssets=[];
let workloadFixture=null, networkFixture=null, actionRequests=0;
const browser=await chromium.launch({channel:'msedge',headless:true});
try {
 const context=await browser.newContext({viewport:{width:1440,height:1000},reducedMotion:'reduce'});
 await context.addInitScript(token=>{Object.defineProperty(window,'__rigmdLocalApiToken',{value:token});window.__startupMessages=[];window.chrome||={};window.chrome.webview={postMessage:m=>window.__startupMessages.push(m)};},token);
 await context.route('**/*',async route=>{
  const url=new URL(route.request().url()),p=url.pathname; if(p.startsWith('/assets/'))loadedAssets.push(p);
  if(p.startsWith('/hubs/')){sensors++;return route.fulfill({status:503,json:{error:'Synthetic test has no sensor stream'}});}
  if(!p.startsWith('/api/'))return route.continue();
  if(p==='/api/home/overview'){homes++;if(homeHeld)await new Promise(resolve=>releaseHome=resolve);if(homeMode==='fail')return route.fulfill({status:503,json:{error:'Synthetic Home failure'}});return route.fulfill({json:{scope:'essential',capturedAt:new Date().toISOString(),deviceName:'TEST-PC',cpu:{name:'Synthetic CPU',usagePercent:readingsAvailable?cpuValue:null,state:readingsAvailable?'measured':'unavailable',source:'Test fixture',capturedAt:readingsAvailable?new Date().toISOString():null,windowSeconds:readingsAvailable?1:null},memory:{totalGb:readingsAvailable?16:null,usedGb:readingsAvailable?memoryValue*16/100:null,usagePercent:readingsAvailable?memoryValue:null},storageQuerySucceeded:readingsAvailable,volumes:[{drive:'X:\\',totalGb:readingsAvailable?100:null,usedGb:readingsAvailable?spaceValue:null,usagePercent:readingsAvailable?spaceValue:null}],network:{querySucceeded:readingsAvailable,hasActiveAdapter:readingsAvailable?true:null,hasIpv4Address:readingsAvailable?hasIp:null,hasDefaultGateway:readingsAvailable?true:null,hasDnsServers:readingsAvailable?true:null,kind:readingsAvailable?'wired':null,ipAddress:readingsAvailable&&hasIp?'192.0.2.10':null}}});}
  if(p==='/api/warning-signs/WS-006/checks'){
    if(linksMode==='hold')await new Promise(resolve=>releaseLinks=resolve);
    if(linksMode==='fail')return route.fulfill({status:503,json:{error:'Synthetic links outage'}});
    const offset=Number(url.searchParams.get('offset'));const rows=Array.from({length:22},(_,i)=>({session_id:`00000000-0000-4000-8000-${String(i+1).padStart(12,'0')}`,created_at:'2026-11-10T12:00:00Z',action_category:'Maintain',resolution_status:'resolved'}));
    return route.fulfill({json:{reference_id:'WS-006',total_checks:22,offset,limit:20,checks:rows.slice(offset,offset+20)}});
  }
  if (/^\/api\/autonomy\/(?:preview|execute|tools\/preview|close-selected-app)/.test(p)) actionRequests++;
  if(networkFixture && /^\/api\/diagnosis\/sessions\/[0-9a-f-]+$/i.test(p)) return route.fulfill({json:{
    session_id:p.split('/').pop(),session_code:'S-SYNTHETIC-NETWORK',created_at:'2026-11-13T00:00:00Z',
    symptom_type:'Device check',diagnosis_mode:'component',component_ids:['network'],diagnosed_category:'Network issue',
    action_category:'Troubleshoot',primary_result:'Synthetic network observation',proof:networkFixture, resolution_proof:[],follow_up_history:[]
  }});
  if(workloadFixture && /^\/api\/diagnosis\/sessions\/[0-9a-f-]+$/i.test(p)) return route.fulfill({json:{
    session_id:p.split('/').pop(),session_code:'S-SYNTHETIC-WORKLOAD',created_at:'2026-11-12T09:00:00Z',
    symptom_type:'Device check',diagnosis_mode:'component',component_ids:[workloadFixture.component],
    diagnosed_category:workloadFixture.category,action_category:'Maintain',confidence_label:'Measured',
    primary_result:'Synthetic workload observation',proof:[{metric_id:workloadFixture.metric,label:'Synthetic workload reading',
      value:workloadFixture.value,status:'high',evidence_kind:'measurement'}],resolution_proof:[],follow_up_history:[]
  }});
  if(/^\/api\/diagnosis\/sessions\/[0-9a-f-]+$/i.test(p)){openedSessionIds.push(p.split('/').pop());return route.fulfill({json:{session_id:p.split('/').pop(),session_code:'S-TEST-1',created_at:'2026-11-10T12:00:00Z',symptom_type:'Device check',diagnosed_category:'Memory pressure',action_category:'Maintain',confidence_label:'Measured',explanation:'Synthetic saved-check evidence',primary_result:'Synthetic saved-check evidence',follow_up_history_status:'legacy_latest_only',follow_up_history:[{"observation_id": "fixture-unknown", "checked_at": "2026-11-11T12:02:00Z", "status": "needs_recheck", "summary": "Synthetic unavailable recheck", "source": "recheck", "proof": [{"metric_id": "memory_usage", "label": "Memory use", "value": "Unavailable", "status": "unknown"}]}, {"observation_id": "fixture-normal", "checked_at": "2026-11-11T12:01:00Z", "status": "resolved", "summary": "Synthetic normal recheck", "source": "recheck", "proof": [{"metric_id": "memory_usage", "label": "Memory use", "value": "40%", "status": "normal"}]}, {"observation_id": "fixture-old", "checked_at": "2026-11-11T12:00:00Z", "status": "still_active", "summary": "Synthetic preserved earlier recheck", "source": "legacy_latest", "proof": [{"metric_id": "memory_usage", "label": "Memory use", "value": "91%", "status": "high"}]}],resolution_status:'resolved',proof:[{metric_id:'memory_usage',label:'Memory usage',value:'95%',status:'high',meaning:'Synthetic recorded high usage'}],resolution_proof:[]}});}
  if(p.endsWith('/feedback')) {
    const id=p.split('/')[4];
    if(route.request().method()==='POST') {
      feedbackSaves++;const body=route.request().postDataJSON();
      const saved={feedback_id:'feedback-'+feedbackSaves,session_id:id,outcome:body.outcome,note:body.note,created_at:new Date().toISOString()};
      feedbackBySession.set(id,[saved,...(feedbackBySession.get(id)??[])]);
      return route.fulfill({json:saved});
    }
    return route.fulfill({json:feedbackBySession.get(id)??[]});
  }
  if(p.endsWith('/check-resolution')) {
    recheckCount++;
    if(recheckMode==='fail')return route.fulfill({status:503,json:{error:'Synthetic recheck failure'}});
    const checked_at=new Date(Date.UTC(2026,10,11,12,recheckCount)).toISOString();
    const status=recheckMode==='clear'?'resolved':recheckMode==='unknown'?'needs_recheck':'still_active';
    const proof=recheckMode==='clear'||recheckMode==='unknown' ? [
      {metric_id:'memory_usage',label:'Memory use',value:recheckMode==='clear'?'78.4%':'Unavailable',status:recheckMode==='clear'?'normal':'unknown'},
      {metric_id:'storage_usage',label:'Storage space',value:'53.8% used',status:'normal'},
      {metric_id:'storage_health',label:'Drive-health warnings',value:'No warning reported',status:'info'}
    ] : [{metric_id:'memory_usage',label:'Memory use',value:'91%',status:'high'}];
    return route.fulfill({json:{resolution_status:status,resolution_checked_at:checked_at,resolution_summary:'Synthetic successful recheck '+recheckCount,
      resolution_proof:proof,follow_up_history_status:'complete',follow_up_history:[{observation_id:'recheck-'+recheckCount,checked_at,status,summary:'Synthetic successful recheck '+recheckCount,source:'recheck',proof}]}});
  }
  if(p.includes('/remediation-eligibility'))return route.fulfill({json:{state:'allowed',message:'Synthetic current check'}});
  if(p==='/api/hardware/live'){inventories++;if(inventoryMode==='hold')await new Promise(resolve=>releaseInventory=resolve);return inventoryMode==='fail'?route.fulfill({status:503,json:{error:'Synthetic unavailable inventory'}}):route.fulfill({json:full});}
  if(p==='/api/diagnosis/sessions'){if(sessionsHeld)await new Promise(resolve=>releaseSessions=resolve);return route.fulfill({json:[]});}
  if(p==='/api/dashboard/summary')return summaryMode==='fail'?route.fulfill({status:503,json:{error:'Synthetic summary outage'}}):route.fulfill({json:summary});
  if(p==='/api/warning-signs/reference'){
   warningRequests++;assert.equal(url.searchParams.get('category'),'all');
   if(alertsMode==='hold')await new Promise(resolve=>releaseAlerts=resolve);
   if(alertsMode==='fail')return route.fulfill({status:503,json:{error:'Synthetic warning outage'}});
   if(alertsMode==='malformed')return route.fulfill({json:{summary:{observed_warning_signs:0},warning_signs:[{}]}});
   return route.fulfill({json:{summary:{observed_warning_signs:1,total_observed_occurrences:2,total_reference_items:2},categories:['Storage','Thermal'],warning_signs:warningRows,...(alertsMode==='database-fail'?{database_warning:'Synthetic database unavailable'}:{})}});
  }
  if(p==='/api/desktop/recovery')return route.fulfill({json:{blocked:recoveryBlocked,message:recoveryBlocked?'Fixture recovery in progress':''}});
  if(p==='/api/cloud-ai/account')return route.fulfill({json:{signedIn:false,email:null}});
  if(p==='/api/cloud-ai/connect')return route.fulfill({json:{state:'idle'}});
  if(p==='/api/performance')return route.fulfill({status:202,json:{accepted:true}});
  return route.fulfill({status:404,json:{error:'Unexpected synthetic API request: '+p}});
 });
 const page=await context.newPage();page.on('console',m=>{if(m.type()==='error')console.log('BROWSER CONSOLE:',m.text());});page.on('requestfailed',r=>console.log('REQUEST FAILED:',r.url(),r.failure()));page.on('pageerror',e=>errors.push(e.message));

 alertsMode='ready';inventoryMode='ready';
 let assetsHeld=true;const waitingAssets=[];
 await context.route('**/assets/*.js',async route=>{if(assetsHeld)await new Promise(resolve=>waitingAssets.push(resolve));return route.fallback();});
 await page.goto(origin,{waitUntil:'commit'});
 await page.locator('#rigmd-boot').waitFor();
 assert.equal(await page.locator('#rigmd-boot img').evaluate(el=>getComputedStyle(el).borderRadius),'0px');
 assert.equal(await page.locator('#rigmd-boot .startup-activity').evaluate(el=>getComputedStyle(el).animationName),'none');
 assert.equal(await page.getByRole('status',{name:'Getting RigMD ready',exact:true}).count(),1);
 assert.equal(await page.locator('#rigmd-boot').evaluate(el=>getComputedStyle(el).backgroundColor),'rgb(7, 12, 18)');
 await page.screenshot({path:path.join(root,'html-before-javascript.png')});
 checks.push('Dark, accessible HTML splash is visible before application JavaScript is available.');
 assetsHeld=false;waitingAssets.forEach(resolve=>resolve());
 await page.locator('#rigmd-boot').waitFor({state:'detached'});
 const splash=page.getByRole('status',{name:'Getting RigMD ready',exact:true});
 await splash.waitFor();
 assert.equal(await splash.getByRole('progressbar').count(),0);
 await splash.locator('img[src="/rigmd-logo.png"]').evaluate(async image=>{await image.decode();if(image.naturalWidth!==256)throw new Error('Official logo did not decode');});
 assert.equal(await splash.locator('img[src="/rigmd-logo.png"]').evaluate(el=>getComputedStyle(el).borderRadius),'0px');
 assert.equal(await page.evaluate(()=>window.__startupMessages.some(m=>m.type==='rigmd:dashboard-ready')),false);
 assert.equal(await splash.locator('.startup-activity').evaluate(el=>getComputedStyle(el).animationName),'none');
 assert.equal(await splash.locator('img').evaluate(el=>el.getBoundingClientRect().width),64);
 await page.screenshot({path:path.join(root,'react-splash-reduced-motion.png')});
 const logoMotion=splash.locator('.startup-activity');
 const still=await logoMotion.evaluate(el=>getComputedStyle(el).transform);await pause(250);
 assert.equal(await logoMotion.evaluate(el=>getComputedStyle(el).transform),still);
 checks.push('React splash stays while essential readings are pending, respects reduced motion, and does not report fake completion.');
 homeHeld=false;releaseHome();
 await splash.waitFor({state:'detached'});
 await page.waitForFunction(()=>window.__startupMessages.some(m=>m.type==='rigmd:dashboard-ready'));
 assert.equal(await page.evaluate(()=>window.__startupMessages.find(m=>m.type==='rigmd:dashboard-ready').outcome),'completed');
 checks.push('Splash exits on actual Home readiness; desktop readiness is reported afterward.');

 async function openCheck(index=0) {
   await page.getByRole('button',{name:'Alerts',exact:true}).click();
   await page.getByRole('button',{name:'View saved checks',exact:true}).click();
   await page.getByRole('button',{name:'Open saved check',exact:true}).nth(index).click();
   await page.getByRole('heading',{name:'Is the problem still happening?',exact:true}).waitFor();
   await page.getByRole('button',{name:'Check again',exact:true}).waitFor();
 }
 await openCheck();
 const feedback=()=>page.getByRole('heading',{name:'Is the problem still happening?',exact:true});
 const note=page.getByRole('textbox',{name:'Anything you want to add?',exact:false});
 await note.fill('Unsaved fixture note survives result updates.');
 for(let i=0;i<4;i++) {
   await page.getByRole('button',{name:'Check again',exact:true}).click();
   await page.getByText('Could not recheck the current issue status.',{exact:true}).waitFor();
   assert.equal(await feedback().count(),1);
   assert.equal(await page.getByRole('heading',{name:'Recheck history',exact:true}).count(),1);
   assert.equal(await note.inputValue(),'Unsaved fixture note survives result updates.');
 }
 checks.push('Four repeated failed rechecks leave exactly one feedback form and one history section; the unsaved note survives.');
 recheckMode='success';
 for(let i=0;i<3;i++) {
   const next=recheckCount+1;
   await page.getByRole('button',{name:'Check again',exact:true}).click();
   await page.getByText('Synthetic successful recheck '+next,{exact:true}).waitFor({state:'attached'});
   assert.equal(await feedback().count(),1);
   assert.equal(await note.inputValue(),'Unsaved fixture note survives result updates.');
 }
 checks.push('Successful rechecks after failures update history without duplicating or resetting the feedback form.');
 await page.getByRole('radio',{name:'Not sure',exact:true}).check();
 await page.getByRole('button',{name:'Save feedback',exact:true}).click();
 await page.getByText('Your answer is saved. The diagnostic readings have not changed.',{exact:true}).waitFor();
 assert.equal(feedbackSaves,1);
 await page.locator('section[aria-labelledby^="feedback-title-"]').screenshot({path:path.join(root,'single-feedback-card.png')});
 checks.push('One Save feedback click sends one request and displays the saved answer in the single form.');
 await page.getByRole('button',{name:'Back to Alerts',exact:true}).click();
 await page.getByRole('button',{name:'View saved checks',exact:true}).click();
 await page.getByRole('button',{name:'Open saved check',exact:true}).nth(1).click();
 await feedback().waitFor();await page.getByRole('button',{name:'Save feedback',exact:true}).waitFor();
 assert.equal(await note.inputValue(),'');assert.equal(await feedback().count(),1);
 await page.getByRole('button',{name:'Back to Alerts',exact:true}).click();
 await page.getByRole('button',{name:'View saved checks',exact:true}).click();
 await page.getByRole('button',{name:'Open saved check',exact:true}).first().click();
 await page.getByRole('button',{name:'Update feedback',exact:true}).waitFor();
 assert.equal(await note.inputValue(),'Unsaved fixture note survives result updates.');
 assert.equal(await feedback().count(),1);
 checks.push('Switching between saved checks isolates form state and reloads the correct saved feedback without duplicate cards.');


 recheckMode='clear';
 await page.getByRole('button',{name:'Check again',exact:true}).click();
 const findings=page.locator('section[aria-label="Findings"]').first();
 const other=findings.getByRole('tab',{name:'Other readings 3',exact:true});
 await other.waitFor();
 assert.equal(await other.getAttribute('aria-selected'),'true');
 await findings.getByRole('button',{name:'View Working memory details',exact:true}).waitFor();
 assert.equal(await findings.getByText('No readings in this group.',{exact:true}).count(),0);
 await findings.screenshot({path:path.join(root,'findings-after-cleared-warning.png')});
 checks.push('A cleared warning automatically reveals the three available readings instead of an empty warning tab.');
 const attention=findings.getByRole('tab',{name:'Needs attention 0',exact:true});
 await attention.click();
 assert.equal(await attention.getAttribute('aria-selected'),'true');
 await findings.getByText('No readings in this group.',{exact:true}).waitFor();
 await other.click();await page.keyboard.press('ArrowLeft');
 assert.equal(await findings.getByRole('tab',{name:'Not verified 0',exact:true}).getAttribute('aria-selected'),'true');
 checks.push('Explicit empty-tab choices and keyboard tab navigation remain available after automatic evidence selection.');
 await page.getByRole('button',{name:'Back to Alerts',exact:true}).click();
 await page.getByRole('button',{name:'View saved checks',exact:true}).click();
 await page.getByRole('button',{name:'Open saved check',exact:true}).first().click();
 await page.getByRole('button',{name:'Check again',exact:true}).waitFor();
 recheckMode='unknown';
 await page.getByRole('button',{name:'Check again',exact:true}).click();
 const unknown=findings.getByRole('tab',{name:'Not verified 1',exact:true});await unknown.waitFor();
 assert.equal(await unknown.getAttribute('aria-selected'),'true');
 await findings.getByRole('button',{name:'View Working memory details',exact:true}).waitFor();
 assert.equal(await feedback().count(),1);
 checks.push('An unavailable follow-up selects Not verified and keeps one feedback form without reporting a normal reading.');

 homeHeld=true;releaseHome=null;await page.emulateMedia({reducedMotion:'no-preference'});
 await page.reload();
 await splash.waitFor();await pause(120);
 const movingBefore=await logoMotion.evaluate(el=>getComputedStyle(el).transform);await pause(450);
 assert.notEqual(await logoMotion.evaluate(el=>getComputedStyle(el).transform),movingBefore);
 await page.screenshot({path:path.join(root,'react-splash-animated.png')});
 await page.setViewportSize({width:390,height:844});
 assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
 await page.screenshot({path:path.join(root,'react-splash-mobile.png')});
 homeHeld=false;releaseHome();await splash.waitFor({state:'detached'});
 checks.push('Subtle activity animation works with normal motion; the professional splash fits 390px without horizontal overflow.');
 homeMode='fail';await page.reload();await splash.waitFor({state:'detached'});
 await page.waitForFunction(()=>window.__startupMessages.some(m=>m.type==='rigmd:dashboard-ready'));
 assert.equal(await page.evaluate(()=>window.__startupMessages.find(m=>m.type==='rigmd:dashboard-ready').outcome),'incomplete');
 checks.push('Failed Home loading ends the splash and records incomplete startup rather than a false success.');
 homeMode='ready';await page.setViewportSize({width:1440,height:1000});
 for(const fixture of [
   {category:'Thermal condition',component:'thermal',metric:'cpu_temperature',value:'90°C',text:'airflow',file:'thermal-guidance.png'},
   {category:'Low Battery Charge',component:'battery',metric:'battery_charge',value:'10%',text:'charging',file:'battery-guidance.png'},
   {category:'Elevated CPU Utilization',component:'cpu',metric:'cpu_usage',value:'95%',text:'Task Manager',file:'cpu-guidance.png'}
 ]) {
   workloadFixture=fixture;await page.reload();
   await page.getByRole('button',{name:'Alerts',exact:true}).click();
   await page.getByRole('button',{name:'View saved checks',exact:true}).click();
   await page.getByRole('button',{name:'Open saved check',exact:true}).first().click();
   const guidance=page.getByRole('region',{name:'Investigation recommended'});await guidance.waitFor();
   assert.ok((await guidance.innerText()).includes(fixture.text));
   assert.equal(await page.getByRole('button',{name:'Review action options',exact:true}).count(),0);
   assert.equal(await page.getByRole('button',{name:'Review apps using memory',exact:true}).count(),0);
   assert.equal(await feedback().count(),1);
   await pause(400);await page.screenshot({path:path.join(root,fixture.file)});
   await page.getByRole('button',{name:'Open a separate memory check',exact:true}).click();
   await page.getByRole('button',{name:'Check selected parts',exact:true}).waitFor();
   checks.push(fixture.category+' shows specific guidance without an unnamed action or duplicate feedback form.');
 }
 workloadFixture=null;
 const connectedNetwork=()=>['network_adapter','network_address','network_gateway','network_dns_config','network_dns'].map(id=>({
   metric_id:id,label:id,value:id==='network_dns'?'Not reported':'Reported',status:id==='network_dns'?'high':'normal',evidence_kind:'measurement'
 }));
 for(const networkCase of [
   {name:'offline-adapter',rows:[{metric_id:'network_adapter',label:'Active IPv4 adapter',value:'Not reported',status:'high',evidence_kind:'measurement'}],allowed:false},
   {name:'missing-gateway',rows:connectedNetwork().map(row=>row.metric_id==='network_gateway'?{...row,value:'Not reported',status:'high'}:row),allowed:false},
   {name:'connected-dns-failure',rows:connectedNetwork(),allowed:true}
 ]) {
   networkFixture=networkCase.rows;
   await page.getByRole('button',{name:'Home',exact:true}).click();
   await page.getByRole('button',{name:'Alerts',exact:true}).click();
   const savedToggle=page.getByRole('button',{name:/^(View|Hide) saved checks$/}).first();
   await savedToggle.waitFor();
   if((await savedToggle.innerText()).startsWith('View')) await savedToggle.click();
   await page.getByRole('button',{name:'Open saved check',exact:true}).first().click();
   await page.getByTestId('recommended-next-steps').waitFor();
   if(networkCase.allowed) await page.getByRole('button',{name:'Review action options',exact:true}).waitFor();
   else {
     await page.getByText(/Refreshing saved website addresses cannot restore a disconnected adapter/).waitFor();
     assert.equal(await page.getByRole('button',{name:'Review action options',exact:true}).count(),0);
   }
   assert.equal(await feedback().count(),1);
   await pause(500);
   await page.screenshot({path:path.join(root,networkCase.name+'.png')});
   checks.push(networkCase.name+': DNS review availability follows saved connection and lookup evidence.');
   await page.getByRole('button',{name:'Back to Alerts',exact:true}).click();
 }
 assert.equal(actionRequests,0);
 assert.deepEqual(errors,[]);
 fs.writeFileSync(path.join(root,'focused-report.json'),JSON.stringify({fixtures:'Synthetic data only; no installed app or user records changed.',checks,errors},null,2));
 console.log(JSON.stringify({checks,errors},null,2));await context.close();
} finally {await browser.close();await new Promise(resolve=>server.close(resolve));}
