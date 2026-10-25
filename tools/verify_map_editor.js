// Run the actual map workflow functions with a minimal DOM. Scene editor has its own regression suite.
const fs=require('fs'),path=require('path'),vm=require('vm'),assert=require('assert');
const root=path.resolve(__dirname,'..'),html=fs.readFileSync(path.join(root,'EC2BUnofficialPatch/ModAuthorTemplate/MapExtend/map-extend.html'),'utf8');
const source=html.match(/<script>([\s\S]*?)<\/script>/)[1];new vm.Script(source);
let checks=0;const check=(ok,name)=>{assert(ok,name);checks++;};
const elements=new Map(),element=id=>{if(!elements.has(id))elements.set(id,{value:'',checked:false,setCustomValidity(v){this.validation=v;}});return elements.get(id);};
const sandbox={$:element,n:id=>Number(element(id).value),message(t){sandbox.lastMessage=t;},pair:v=>Array.isArray(v)&&v.length===2&&v.every(Number.isFinite),validPath:v=>/^Images\/.+\.png$/i.test(v),draw(){},refreshWorlds(){},updateWorldSummary(){},showCfg(){},selectPanel(){},selectWorld(id){sandbox.activeWorldId=id;},save(){},lastMessage:''};
const ctx=vm.createContext(sandbox),run=s=>vm.runInContext(s,ctx);
run("let worldData={schema:1,worlds:[{id:0,name:'鹅城',mapIds:[12],autoUnlockMaps:[],travelMaps:[12]},{id:72,name:'测试区',mapIds:[31,32],autoUnlockMaps:[31,31001],travelMaps:[31]}]},nativeCfg={MapCfg:{31:{id:31,type:0,bg:201,floors:[31,31001]},32:{id:32,type:0,bg:202,floors:[]},31001:{id:31001,type:2,bg:203,floors:[]}},BgCfg:{}},database={locations:[]},dirty=false,worldDirty=false,cfgDirty=false,projectPending=false,cfgEditingTable='MapCfg'; const worldPictures=new Map(),layouts=new Map(),assets=new Map();");
sandbox.activeWorldId=72;
// Functions have no string braces; scan braces to select the exact production function.
function production(name){const start=source.indexOf('function '+name+'(');assert(start>=0,name);let i=source.indexOf('{',start),depth=1;for(i++;depth;i++){if(source[i]==='{')depth++;else if(source[i]==='}')depth--;}return source.slice(start,i);}
run(source.split('\n').find(l=>l.startsWith('const ids=')));
run(source.split('\n').find(l=>l.startsWith('const currentWorld=')));
run(source.split('\n').find(l=>l.startsWith('const parentDefaults=')));
for(const name of ['isChildUnlock','parentUnlockIds','requiredTravelParents','renderParentUnlock','parseParentUnlock','syncParentUnlockText','syncParentUnlockCheckbox','validateChildId','createWorld','validateTable','generateParentCfg','saveWorldSettings','applyCfg','addChild'])run(production(name));
const getState=()=>run('JSON.stringify({worldData,nativeCfg,database,projectPending,cfgDirty,dirty})');
const field=(id,value)=>element(id).value=String(value);
function rejects(code,name){let bad=false;try{run(code);}catch{bad=true;}check(bad,name);}
field('mapId',31);element('station').checked=true;run('renderParentUnlock()');check(element('extraUnlock').value==='31','parent list excludes child');check(element('entryAutoUnlock').checked,'checkbox reflects stored parent');
check(element('entryAutoUnlock').disabled,'transport auto-open is locked');
element('entryAutoUnlock').checked=false;run('syncParentUnlockCheckbox()');check(element('entryAutoUnlock').checked,'direct transport uncheck is corrected');
field('extraUnlock','32');run('syncParentUnlockText()');check(element('entryAutoUnlock').checked&&element('extraUnlock').value.includes('31'),'list cannot remove mandatory transport');
check(run('currentWorld().autoUnlockMaps.includes(31001)'),'mandatory parent does not erase child selection');
element('station').checked=false;run('currentWorld().travelMaps=[];renderParentUnlock()');check(!element('entryAutoUnlock').disabled,'removing transport re-enables checkbox');check(element('entryAutoUnlock').checked,'removing transport does not uncheck auto-open');
field('extraUnlock','32');run('syncParentUnlockText()');check(!element('entryAutoUnlock').checked,'list removal unchecks parent');check(run('currentWorld().autoUnlockMaps.includes(31001)'),'list edit retains child selection');
field('mapId',32);run('renderParentUnlock()');check(element('entryAutoUnlock').checked,'switch parent uses list selection');
element('entryAutoUnlock').checked=false;run('syncParentUnlockCheckbox()');check(element('extraUnlock').value==='','checkbox removal removes list ID');
element('entryAutoUnlock').checked=true;run('syncParentUnlockCheckbox()');check(element('extraUnlock').value==='32','checkbox adds ID');run('syncParentUnlockCheckbox()');check(run('currentWorld().autoUnlockMaps.filter(id=>id===32).length===1'),'deduplicated live sync');
check(!element('station').checked,'auto open does not mark transport');
let before=getState();field('extraUnlock','31001');run('syncParentUnlockText()');check(getState()===before,'child ID rejected in parent list without mutation');
field('extraUnlock','32, abc');run('syncParentUnlockText()');check(getState()===before,'invalid list preserves project');run('renderParentUnlock()');
field('mapId','');element('entryAutoUnlock').checked=true;run('syncParentUnlockCheckbox()');check(!element('entryAutoUnlock').checked,'blank parent ID cannot be selected');check(getState()===before,'blank parent leaves project untouched');
for(const id of [32002,31,31001,2147483648,31002.5,0])rejects('validateChildId(31,'+id+')','invalid or occupied child '+id);
run('validateChildId(31,31000)');checks++;run('validateChildId(31,31002)');checks++;run('validateChildId(31,31999)');checks++;
rejects('validateChildId(2147484,2147484001)','overflow parent');run('validateChildId(2147483,2147483647)');checks++;
field('mapId',31);field('childId',32002);field('childName','无效子地点');field('childBg',203);element('generateParent').checked=true;field('parentBg',999);field('name','不应覆盖父地点');before=getState();rejects('addChild()','reject invalid child before parent generation');check(getState()===before,'invalid child does not overwrite parent, CFG or flags');
field('childId',31002);field('childName','合法子地点');field('parentFloors','31,31001');element('childAutoUnlock').checked=true;run('addChild()');check(run('nativeCfg.MapCfg[31002].type===2'),'generated child always type 2');check(run('nativeCfg.MapCfg[31].floors.includes(31002)'),'child added to parent floors');check(run('currentWorld().autoUnlockMaps.includes(31002)'),'child auto open exported independently');check(!element('extraUnlock').value.includes('31002'),'child does not leak into parent list');
// Dirty JSON must also remain uncommitted on invalid child ID.
run('cfgDirty=true');field('cfgEditor','{"31":{"id":31,"type":0,"bg":201}}');field('childId',32003);before=getState();rejects('addChild()','invalid child with pending CFG');check(getState()===before,'pending CFG untouched after invalid child');run('cfgDirty=false;dirty=false');
for(const id of [0,-1,72,16777216,1.5]){before=getState();rejects('createWorld('+id+',"")','invalid or duplicate world '+id);check(getState()===before,'invalid world leaves no ghost '+id);}
run('createWorld(123,"")');check(run('currentWorld().name')==='新区域123','blank optional name defaults from manual ID');run('createWorld(124,"  自定义区域  ")');check(run('currentWorld().name')==='自定义区域','optional name accepted');
check(html.includes('<details open class="adjacent-import">'),'world import expanded without separator');check(html.includes('<details open><summary>大地图底图与校准资料'),'calibration expanded');
check(html.indexOf('id="layout"')<html.indexOf('第一次制作')&&html.indexOf('第一次制作')<html.indexOf('id="existing"'),'first-use notice beside layout');check(!html.includes('id="childType"'),'child type selector removed');check(!html.includes('内部地点'),'child terminology consistent');check(!source.includes('Math.max(990001'),'no automatic ID allocation');
check(source.includes("$('entryAutoUnlock').disabled=station"),'transport constraint applied in production UI');
check(html.includes('建议子地点尾号从 001 开始，不建议直接使用 000 尾号'),'warn against suffix 000 while retaining exact validation');
console.log(`PASS ${checks} map editor workflow checks.`);
