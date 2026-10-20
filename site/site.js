const picker=document.getElementById('theme');
const viewer=document.getElementById('viewer');
function applyTheme(value){if(!['coldain','sunstrider','shadowman'].includes(value))value='coldain';document.documentElement.dataset.theme=value;picker.value=value;document.querySelectorAll('[data-screen]').forEach(img=>{img.src=`images/${value}-${img.dataset.screen}.webp`});try{localStorage.setItem('eql-theme',value)}catch{}}
try{applyTheme(localStorage.getItem('eql-theme')||'coldain')}catch{applyTheme('coldain')}
picker.addEventListener('change',()=>applyTheme(picker.value));
document.querySelectorAll('.shot').forEach(button=>button.addEventListener('click',()=>{const original=button.querySelector('img');viewer.querySelector('img').src=original.src;viewer.querySelector('img').alt=original.alt;viewer.showModal()}));
document.getElementById('close-viewer').addEventListener('click',()=>viewer.close());
viewer.addEventListener('click',event=>{if(event.target===viewer)viewer.close()});
