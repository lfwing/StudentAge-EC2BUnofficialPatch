// Can also run in the console of an already-open older MapExtend tool.
async function rescueMapExtend(){
 const report=[],form=[...document.querySelectorAll('input,select,textarea')].filter(e=>e.id&&e.type!=='file').map(e=>({id:e.id,value:e.value,checked:e.checked}));
 try{if(sceneEditor.hasDraft())sceneEditor.save();if(typeof worldDirty!=='undefined'&&worldDirty)saveWorldSettings();if(typeof cfgDirty!=='undefined'&&cfgDirty)applyCfg();if(typeof dirty!=='undefined'&&dirty)save();}catch(e){report.push('未完成表单保存：'+e.message+'；当前表单原文已另外保存。');}
 const json=v=>new Blob([JSON.stringify(v,null,2)+'\n'],{type:'application/json'}),scene=sceneEditor.data(),world=typeof exportWorldData==='function'?exportWorldData():worldData,files=new Map(),source=new Map(retainedFiles);
 // Snapshot all text/config data before attempting any local file read.
 files.set('MapExtend/SceneObjectscfg.json',json(scene));files.set('MapExtend/Worldmapscfg.json',json(world));files.set('MapExtend/MapLocationscfg.json',json(database));
 for(const [table,rows]of Object.entries(nativeCfg))if(Object.keys(rows).length)files.set('MapExtend/Cfg/'+table+'.json',json(rows));
 files.set('MapExtend/readme.txt',new Blob([packageReadme]));files.set('MapExtend/map-extend.html',new Blob([toolSource],{type:'text/html'}));
 files.set('未保存表单.json',json({form,sceneDrafts:sceneEditor.recoveryData?.()||null}));
 const images=new Set();for(const w of world.worlds||[])if(w.id!==0&&!w.extension&&w.url)images.add(w.url);for(const r of [...scene.objects||[],...database.locations||[]])for(const v of Object.values(r?.visual||{}))if(v?.image)images.add(v.image);
 for(const [path,a]of globalAssets())if(a?.file)source.set(path,a.file);
 for(const path of new Set([...source.keys(),...images])){
  if(/^Editor\//i.test(path)||files.has('MapExtend/'+path)||['scene-objects.html','map-extend.html'].includes(path))continue;
  try{const f=source.get(path);if(!f)throw Error('无文件引用');files.set('MapExtend/'+path,new Blob([await f.arrayBuffer()],{type:f.type||''}));}
  catch(e){
   const im=sceneEditor.decodedImage?.(path)||globalAssets().get(path)?.image;
   if(/\.png$/i.test(path)&&im){try{const c=document.createElement('canvas');c.width=im.naturalWidth||im.width;c.height=im.naturalHeight||im.height;if(!c.width||!c.height)throw Error('图片无尺寸');c.getContext('2d').drawImage(im,0,0);const b=await new Promise(resolve=>c.toBlob(resolve,'image/png'));if(!b)throw Error('PNG转换失败');files.set('MapExtend/'+path,b);report.push('图片由内存解码内容重建（PNG字节与原文件可能不同）：'+path);continue;}catch{}}
   report.push('仍需从原文件补入：'+path+'（'+e.message+'）');
  }
 }
 report.unshift('这是抢救备份，请先解压检查。配置、对象、选区来自当前网页内存；普通导出失败并不代表它们被清空。参考图仍不打包。未保存表单.json只供核对，工具不会自动将非法/未生成表单写回原版表。');
 files.set('抢救说明.txt',new Blob([report.join('\n')],{type:'text/plain;charset=utf-8'}));
 downloadBlob(await zipFiles(files),'MapExtend-抢救备份.zip');message('抢救备份已下载。请查看抢救说明，确认配置、选区与图片完整后再关闭原页面。');return report;
}
