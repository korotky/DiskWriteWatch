const $ = id => document.getElementById(id);
let refreshSeconds = 10;
let refreshTimer;
let lastChartPoints = [];
let chartDragStart = null;
let chartDragEnd = null;
const bytes = n => { const u = ['B','KiB','MiB','GiB','TiB']; let i=0; while(n>=1024&&i<u.length-1){n/=1024;i++} return `${n.toFixed(i?2:0)} ${u[i]}` };
const pad = n => String(n).padStart(2,'0');
const toLocalInput = seconds => {
  const d = new Date(seconds*1000);
  return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
};
const fromLocalInput = value => {
  const time = Date.parse(value);
  return Number.isFinite(time) ? Math.floor(time/1000) : null;
};
function range(){
  if($('range').value==='custom'){
    const from=fromLocalInput($('fromTime').value), to=fromLocalInput($('toTime').value);
    if(from!==null&&to!==null&&to>from)return {from,to,custom:true};
  }
  const seconds=Number.isFinite(Number($('range').value))?Number($('range').value):3600;
  const to=Math.floor(Date.now()/1000), from=to-seconds;
  $('fromTime').value=toLocalInput(from);$('toTime').value=toLocalInput(to);
  return {from,to,custom:false};
}
async function get(url){const r=await fetch(url);if(!r.ok)throw new Error(`${r.status} ${await r.text()}`);return r.json()}
const filterInputs = ['processFilter','pathFilter','volumeFilter','diskFilter'];
function query(){
  const r=range(), p=new URLSearchParams({from:r.from,to:r.to,includeMonitor:$('includeMonitor').checked});
  for(const [key,id] of [['process','processFilter'],['path','pathFilter'],['volume','volumeFilter'],['disk','diskFilter']]){
    const value=$(id).value.trim(); if(value)p.set(key,value);
  }
  return p.toString();
}
function setFilter(id,value){
  $(id).value=value;
  if(id==='diskFilter')$('volumeFilter').value='';
  if(id==='volumeFilter')$('diskFilter').value='';
  load();
}
function clearFilters(){
  for(const id of filterInputs)$(id).value='';
  load();
}
function applyTime(){
  $('range').value='custom';
  load();
}
function liveRange(){
  $('range').value='3600';
  load();
}
function setCustomRange(from,to){
  if(to<from)[from,to]=[to,from];
  if(to-from<10)return;
  $('range').value='custom';
  $('fromTime').value=toLocalInput(from);
  $('toTime').value=toLocalInput(to);
  load();
}
function table(id,rows,filterId){
  const target=$(id);
  target.innerHTML='<tr><th>Name</th><th>Writes</th><th>Ops</th></tr>'+rows.map(x=>`<tr class="clickable-row" title="Click to filter by ${escapeHtml(x.name)}" data-filter="${filterId}" data-value="${escapeHtml(x.name)}"><td>${escapeHtml(x.name)}</td><td>${bytes(x.bytes)}</td><td>${x.operations.toLocaleString()}</td></tr>`).join('');
  for(const row of target.querySelectorAll('tr[data-filter]')){
    row.onclick=()=>setFilter(row.dataset.filter,row.dataset.value);
  }
}
function disks(items){
  $('disks').innerHTML=items.map(d=>{
    const volumes=(d.volumes||[]).map(v=>`<button class="chip" type="button" data-volume="${escapeHtml(v)}">${escapeHtml(v)}</button>`).join('')||'<span class="muted">no mounted volumes</span>';
    return `<div class="disk-row" title="Click to filter physical writes by disk ${d.number}" data-disk="${d.number}"><span>Disk ${d.number}: ${escapeHtml(d.model)}</span><span class="chips">${volumes}</span></div>`;
  }).join('');
  for(const row of $('disks').querySelectorAll('.disk-row')){
    row.onclick=event=>{
      if(event.target.closest('.chip'))return;
      setFilter('diskFilter',row.dataset.disk);
    };
  }
  for(const chip of $('disks').querySelectorAll('.chip')){
    chip.onclick=event=>{
      event.stopPropagation();
      setFilter('volumeFilter',chip.dataset.volume);
    };
  }
}
function escapeHtml(s){return String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}
function chart(points){
  lastChartPoints=points;
  const c=$('chart'),dpr=devicePixelRatio||1,w=c.clientWidth,h=250;c.width=w*dpr;c.height=h*dpr;
  const x=c.getContext('2d');x.scale(dpr,dpr);x.clearRect(0,0,w,h);if(!points.length)return;
  const top=8,bottom=28,plotH=h-top-bottom;
  const start=points[0].bucketUnixSeconds,end=points[points.length-1].bucketUnixSeconds;
  const span=Math.max(1,end-start);
  const max=Math.max(1,...points.flatMap(p=>[p.logicalBytes,p.physicalBytes]));
  const tickLabel = seconds => {
    const d = new Date(seconds*1000);
    return span > 86400
      ? d.toLocaleDateString([], {month:'2-digit',day:'2-digit'})+' '+d.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'})
      : d.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'});
  };
  x.font='11px system-ui,Segoe UI,sans-serif';x.fillStyle='#91a0b8';x.strokeStyle='#253247';x.lineWidth=1;
  for(let i=0;i<=5;i++){
    const px=i/5*w,ts=start+span*i/5;
    x.beginPath();x.moveTo(px,top);x.lineTo(px,top+plotH);x.stroke();
    const label=tickLabel(ts);
    const textW=x.measureText(label).width;
    x.fillText(label,Math.min(Math.max(2,px-textW/2),w-textW-2),h-8);
  }
  const pointX = p => span===0 ? 0 : (p.bucketUnixSeconds-start)/span*w;
  const draw=(key,color)=>{x.strokeStyle=color;x.lineWidth=2;x.beginPath();points.forEach((p,i)=>{const px=pointX(p),py=top+plotH-p[key]/max*(plotH-6);i?x.lineTo(px,py):x.moveTo(px,py)});x.stroke()};
  draw('logicalBytes','#5aa9ff');draw('physicalBytes','#ffad5a');
  points.forEach(p=>{const px=pointX(p);if(!p.persisted){x.fillStyle='#53d18c';x.fillRect(px,h-bottom+4,3,5)}if(p.anomaly){x.fillStyle='#ff6b73';x.beginPath();x.arc(px,top,4,0,Math.PI*2);x.fill()}});
  if(chartDragStart!==null&&chartDragEnd!==null){
    const left=Math.min(chartDragStart,chartDragEnd),right=Math.max(chartDragStart,chartDragEnd);
    x.fillStyle='rgba(90,169,255,.18)';x.fillRect(left,top,right-left,plotH);
    x.strokeStyle='#5aa9ff';x.strokeRect(left,top,right-left,plotH);
  }
}
function chartTimestamp(clientX){
  if(lastChartPoints.length<2)return null;
  const rect=$('chart').getBoundingClientRect();
  const x=Math.min(Math.max(0,clientX-rect.left),rect.width);
  const start=lastChartPoints[0].bucketUnixSeconds,end=lastChartPoints[lastChartPoints.length-1].bucketUnixSeconds;
  return Math.round(start+(end-start)*x/Math.max(1,rect.width));
}
async function load(){
  try{
    const q=query();
    const [health,timeline,processes,paths,dirs,extensions,config]=await Promise.all([
      get('/api/health'),get('/api/timeline?'+q),get('/api/top/processes?'+q),get('/api/top/paths?'+q),
      get('/api/top/directories?'+q),get('/api/top/extensions?'+q),get('/api/config')]);
    $('status').textContent=health.etwActive?'ETW active':'ETW inactive';$('status').className='pill '+(health.etwActive?'ok':'bad');
    $('pending').textContent=health.pendingBuckets;$('database').textContent=bytes(health.databaseBytes);
    $('logical').textContent=bytes(timeline.reduce((a,b)=>a+b.logicalBytes,0));$('physical').textContent=bytes(timeline.reduce((a,b)=>a+b.physicalBytes,0));
    disks(health.disks);
    $('error').textContent=health.lastError||'';table('processes',processes,'processFilter');table('paths',paths,'pathFilter');table('directories',dirs,'pathFilter');table('extensions',extensions,'pathFilter');chart(timeline);
    refreshSeconds=config.refreshSeconds||10;const r=range();$('export').href=`/api/export.csv?${q}`;
  }catch(e){$('status').textContent='Dashboard error';$('status').className='pill bad';$('error').textContent=e.stack||e}
  clearTimeout(refreshTimer);refreshTimer=setTimeout(load,refreshSeconds*1000);
}
$('refresh').onclick=load;$('clearFilters').onclick=clearFilters;$('applyTime').onclick=applyTime;$('liveRange').onclick=liveRange;$('includeMonitor').onchange=load;
$('range').onchange=()=>{if($('range').value==='custom'&&!$('fromTime').value)range();load()};
$('fromTime').onchange=applyTime;$('toTime').onchange=applyTime;
for(const id of filterInputs){
  $(id).onchange=()=>{
    if(id==='diskFilter'&&$(id).value.trim())$('volumeFilter').value='';
    if(id==='volumeFilter'&&$(id).value.trim())$('diskFilter').value='';
    load();
  };
}
$('chart').onpointerdown=event=>{
  if(lastChartPoints.length<2)return;
  $('chart').setPointerCapture(event.pointerId);
  chartDragStart=event.offsetX;chartDragEnd=event.offsetX;chart(lastChartPoints);
};
$('chart').onpointermove=event=>{
  if(chartDragStart===null)return;
  chartDragEnd=event.offsetX;chart(lastChartPoints);
};
$('chart').onpointerup=event=>{
  if(chartDragStart===null)return;
  const from=chartTimestamp(event.clientX),to=chartTimestamp($('chart').getBoundingClientRect().left+chartDragStart);
  chartDragStart=null;chartDragEnd=null;chart(lastChartPoints);
  if(from!==null&&to!==null)setCustomRange(from,to);
};
load();
