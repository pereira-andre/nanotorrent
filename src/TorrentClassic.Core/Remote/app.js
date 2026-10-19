'use strict';
const $ = id => document.getElementById(id);
let token = sessionStorage.getItem('tc-token') || '', selected = new Set(), currentFilesId = null, polling = false;
const bytes = n => { if (n == null) return '—'; const u=['B','KiB','MiB','GiB','TiB']; let i=0; while(n>=1024&&i<4){n/=1024;i++;} return `${n.toFixed(i?1:0)} ${u[i]}`; };
function message(text) { $('message').textContent = text; }
async function api(path, options={}) {
  const headers = {Authorization:`Bearer ${token}`, ...options.headers};
  if (options.body && typeof options.body === 'string') headers['Content-Type']='application/json';
  const response = await fetch('/api/'+path, {...options,headers});
  if(response.status===401){logout();throw new Error('Invalid access key.');}
  if(!response.ok){let body={};try{body=await response.json();}catch{}throw new Error(body.error||`Request failed (${response.status}).`);}
  const body = await response.text(); return body ? JSON.parse(body) : null;
}
function logout(){token='';sessionStorage.removeItem('tc-token');$('dashboard').hidden=true;$('login').hidden=false;$('logout').hidden=true;selected.clear();}
function cell(row,text){const td=document.createElement('td');td.textContent=text;row.append(td);return td;}
function button(label,action){const b=document.createElement('button');b.textContent=label;b.addEventListener('click',()=>run(action));return b;}
async function run(action){try{await action();}catch(e){message(e.message);}}
async function refresh(){
  if(!token||polling||document.hidden)return;polling=true;
  try{
    const status=await api('status');$('dashboard').hidden=false;$('login').hidden=true;$('logout').hidden=false;
    $('count').textContent=`(${status.torrents.length})`;$('down').textContent='↓ '+bytes(status.downloadRate)+'/s';$('up').textContent='↑ '+bytes(status.uploadRate)+'/s';$('schedule').textContent='Scheduler: '+status.schedule;
    if(document.activeElement!==$('limit-down'))$('limit-down').value=status.downloadLimitKiB;
    if(document.activeElement!==$('limit-up'))$('limit-up').value=status.uploadLimitKiB;
    const existing=new Set(status.torrents.map(t=>t.id));selected=new Set([...selected].filter(id=>existing.has(id)));
    const fragment=document.createDocumentFragment(),query=$('search').value.toLowerCase();
    for(const t of status.torrents.filter(t=>t.name.toLowerCase().includes(query))){
      const row=document.createElement('tr');row.classList.toggle('selected',selected.has(t.id));
      const check=document.createElement('input');check.type='checkbox';check.checked=selected.has(t.id);check.setAttribute('aria-label','Select '+t.name);
      check.addEventListener('change',()=>{check.checked?selected.add(t.id):selected.delete(t.id);row.classList.toggle('selected',check.checked);});cell(row,'').append(check);
      cell(row,t.order);cell(row,t.name);cell(row,bytes(t.size));const progress=cell(row,'');const bar=document.createElement('progress');bar.max=100;bar.value=t.progress;progress.append(bar);const percent=document.createElement('small');percent.textContent=t.progress.toFixed(1)+'%';progress.append(percent);
      cell(row,t.status+(t.forced?' · forced':''));cell(row,bytes(t.downloadRate)+'/s');cell(row,bytes(t.uploadRate)+'/s');cell(row,t.ratio.toFixed(3));cell(row,'').append(button('Files',()=>showFiles(t.id,t.name)));fragment.append(row);
    }
    $('torrents').replaceChildren(fragment);$('empty').hidden=status.torrents.length>0;
  }finally{polling=false;}
}
async function feeds(){
  const fragment=document.createDocumentFragment();
  for(const feed of await api('rss')){
    const section=document.createElement('div');section.className='feed';const head=document.createElement('div');const name=document.createElement('strong');name.textContent=feed.name;head.append(name,button('Refresh',async()=>{await api(`rss/${feed.id}/refresh`,{method:'POST'});await feeds();}));section.append(head);
    if(feed.error){const error=document.createElement('p');error.className='muted';error.textContent=feed.error;section.append(error);}
    for(const item of feed.items.slice(0,25)){const row=document.createElement('div');row.className='feed-item';const title=document.createElement('span');title.textContent=item.title;row.append(title);if(item.downloaded){const done=document.createElement('small');done.textContent='Added';row.append(done);}else row.append(button('Download',async()=>{await api(`rss/${feed.id}/download/${item.id}`,{method:'POST'});await refresh();await feeds();}));section.append(row);}fragment.append(section);
  }
  if(!fragment.childNodes.length){const empty=document.createElement('p');empty.className='muted';empty.textContent='No feeds configured.';fragment.append(empty);}$('feeds').replaceChildren(fragment);
}
async function showFiles(id,name){currentFilesId=id;$('file-title').textContent=name;$('file-panel').hidden=false;const fragment=document.createDocumentFragment();for(const f of await api(`torrents/${id}/files`)){const label=document.createElement('label');const check=document.createElement('input');check.type='checkbox';check.dataset.path=f.path;label.append(check,document.createTextNode(` ${f.path} · ${bytes(f.size)} · ${f.progress.toFixed(1)}%`));fragment.append(label);}$('files').replaceChildren(fragment);}
$('login-form').addEventListener('submit',e=>{e.preventDefault();token=$('token').value.trim();run(async()=>{await refresh();sessionStorage.setItem('tc-token',token);$('token').value='';message('');await feeds();});});
$('logout').addEventListener('click',logout);
$('theme').addEventListener('click',()=>{document.body.classList.toggle('dark');sessionStorage.setItem('tc-dark',document.body.classList.contains('dark'));});
document.body.classList.toggle('dark',sessionStorage.getItem('tc-dark')==='true'||(!sessionStorage.getItem('tc-dark')&&matchMedia('(prefers-color-scheme:dark)').matches));
$('add-form').addEventListener('submit',e=>{e.preventDefault();run(async()=>{await api('torrents',{method:'POST',body:JSON.stringify({source:$('source').value.trim(),start:true})});$('source').value='';message('Torrent added.');await refresh();});});
$('upload').addEventListener('change',()=>run(async()=>{const file=$('upload').files[0];if(!file)return;if(file.size>4194304)throw new Error('The torrent exceeds 4 MiB.');await api('upload',{method:'POST',body:file,headers:{'Content-Type':'application/x-bittorrent'}});$('upload').value='';message('Torrent added.');await refresh();}));
document.querySelectorAll('[data-action]').forEach(b=>b.addEventListener('click',()=>run(async()=>{if(!selected.size){message('Select at least one torrent.');return;}if(b.dataset.action==='remove'&&!confirm('Remove from the list? Files will be kept on disk.'))return;for(const id of selected)await api(`torrents/${id}/${b.dataset.action}`,{method:'POST'});message('Action completed.');await refresh();})));
$('search').addEventListener('input',()=>run(refresh));$('select-all').addEventListener('change',()=>{$('torrents').querySelectorAll('input[type=checkbox]').forEach(c=>{c.checked=$('select-all').checked;c.dispatchEvent(new Event('change'));});});
$('limits-form').addEventListener('submit',e=>{e.preventDefault();run(async()=>{await api('limits',{method:'POST',body:JSON.stringify({download:Number($('limit-down').value),upload:Number($('limit-up').value)})});message('Limits saved.');});});
$('close-files').addEventListener('click',()=>{$('file-panel').hidden=true;currentFilesId=null;});
$('set-priority').addEventListener('click',()=>run(async()=>{const paths=[...$('files').querySelectorAll('input:checked')].map(c=>c.dataset.path);if(!paths.length)throw new Error('Select the files.');await api(`priority/${currentFilesId}`,{method:'POST',body:JSON.stringify({paths,priority:Number($('priority').value)})});message('Priorities saved.');}));
setInterval(()=>run(refresh),3000);setInterval(()=>{if(token&&!document.hidden)run(feeds);},60000);if(token)run(async()=>{await refresh();await feeds();});
