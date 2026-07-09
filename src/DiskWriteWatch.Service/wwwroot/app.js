const $ = id => document.getElementById(id);
let refreshSeconds = 10;
let refreshTimer;
const bytes = n => { const u = ['B','KiB','MiB','GiB','TiB']; let i=0; while(n>=1024&&i<u.length-1){n/=1024;i++} return `${n.toFixed(i?2:0)} ${u[i]}` };
const range = () => { const to=Math.floor(Date.now()/1000), from=to-Number($('range').value); return {from,to} };
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
  load();
}
function clearFilters(){
  for(const id of filterInputs)$(id).value='';
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
  const c=$('chart'),dpr=devicePixelRatio||1,w=c.clientWidth,h=220;c.width=w*dpr;c.height=h*dpr;
  const x=c.getContext('2d');x.scale(dpr,dpr);x.clearRect(0,0,w,h);if(!points.length)return;
  const max=Math.max(1,...points.flatMap(p=>[p.logicalBytes,p.physicalBytes]));
  const draw=(key,color)=>{x.strokeStyle=color;x.lineWidth=2;x.beginPath();points.forEach((p,i)=>{const px=i/Math.max(1,points.length-1)*w,py=h-10-p[key]/max*(h-25);i?x.lineTo(px,py):x.moveTo(px,py)});x.stroke()};
  draw('logicalBytes','#5aa9ff');draw('physicalBytes','#ffad5a');
  points.forEach((p,i)=>{const px=i/Math.max(1,points.length-1)*w;if(!p.persisted){x.fillStyle='#53d18c';x.fillRect(px,h-7,3,5)}if(p.anomaly){x.fillStyle='#ff6b73';x.beginPath();x.arc(px,8,4,0,Math.PI*2);x.fill()}});
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
$('refresh').onclick=load;$('clearFilters').onclick=clearFilters;$('range').onchange=load;$('includeMonitor').onchange=load;
for(const id of filterInputs)$(id).onchange=load;
load();
