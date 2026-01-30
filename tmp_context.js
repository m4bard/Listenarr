const fs=require('fs');
const s=fs.readFileSync('fe/src/views/settings/GeneralSettingsTab.vue','utf8');
const marker='<!-- Proxy Security Modal';
const i=s.indexOf(marker);
console.log('index',i);
if(i>=0) console.log(s.slice(i-400,i+800));
else console.log('marker not found');
