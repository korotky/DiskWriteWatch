const $ = id => document.getElementById(id);
let refreshSeconds = 10;
let refreshTimer;
const bytes = n => { const u = ['B','KiB','MiB','GiB','TiB']; let i=0; while(n>=1024&&i<u.length-1){n/=1024;i++} return `${n.toFixed(i?2:0)} ${u[i]}` };
const range = () => { const to=Math.floor(Date.now()/1000), from=to-Number($('range').value); return {from,to} };
async function get(url){const r=await fetch(url);if(!r.ok)throw new Error(`${r.status} ${await r.text()}`);return r.json()}
function query(){
  const r=range(), p=new URLSearchParams({from:r.from,to:r.to,includeMonitor:$('includeMonitor').checked});
  for(const [key,id] of [['process','processFilter'],['path','pathFilter'],['volume','volumeFilter'],['disk','diskFilter']]){
    const value=$(id).value.trim(); if(value)p.set(key,value);
  }
  return p.toString();
}
function table(id,rows){$(id).innerHTML='<tr><th>Name</th><th>Writes</th><th>Ops</th></tr>'+rows.map(x=>`<tr title="${escapeHtml(x.name)}"><td>${escapeHtml(x.name)}</td><td>${bytes(x.bytes)}</td><td>${x.operations.toLocaleString()}</td></tr>`).join('')}
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
    $('disks').innerHTML=health.disks.map(d=>`<div>Disk ${d.number}: ${escapeHtml(d.model)} (${d.volumes.join(', ')||'no mounted volumes'})</div>`).join('');
    $('error').textContent=health.lastError||'';table('processes',processes);table('paths',paths);table('directories',dirs);table('extensions',extensions);chart(timeline);
    refreshSeconds=config.refreshSeconds||10;const r=range();$('export').href=`/api/export.csv?${q}`;
  }catch(e){$('status').textContent='Dashboard error';$('status').className='pill bad';$('error').textContent=e.stack||e}
  clearTimeout(refreshTimer);refreshTimer=setTimeout(load,refreshSeconds*1000);
}
$('refresh').onclick=load;$('range').onchange=load;$('includeMonitor').onchange=load;
for(const id of ['processFilter','pathFilter','volumeFilter','diskFilter'])$(id).onchange=load;
load();
