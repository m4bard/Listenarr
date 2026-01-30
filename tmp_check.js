const fs=require('fs');
const s=fs.readFileSync('fe/src/views/settings/GeneralSettingsTab.vue','utf8');
const t=s.match(/<template>[\s\S]*<\/template>/);
if(!t){console.log('no template'); process.exit(1);} 
const tpl=t[0];
const opens=(tpl.match(/<div\b/g)||[]).length;
const closes=(tpl.match(/<\/div>/g)||[]).length;
console.log('div opens',opens,'div closes',closes);
console.log('template len',tpl.length);

// Also print last 300 chars of template
console.log('\n--- template tail ---\n');
console.log(tpl.slice(-500));
