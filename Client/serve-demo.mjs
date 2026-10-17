// Standalone offline preview. Live play is served by the RimWorld mod itself.
import http from 'node:http';
import {readFile} from 'node:fs/promises';
const files = {'/':'index.html','/index.html':'index.html','/app.js':'app.js','/core.mjs':'core.mjs','/style.css':'style.css'};
const types = {html:'text/html',js:'text/javascript',mjs:'text/javascript',css:'text/css'};
const port=Number(process.env.RIMFRONT_DEMO_PORT||18742);
http.createServer(async (req,res)=>{
  const file=files[new URL(req.url,'http://127.0.0.1').pathname];
  if(!file || req.method!=='GET'){res.writeHead(404);res.end();return;}
  try {
    const bytes=await readFile(new URL(file,import.meta.url));
    res.writeHead(200,{'Content-Type':types[file.split('.').pop()]+'; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff','Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'"});
    res.end(bytes);
  } catch {res.writeHead(500);res.end();}
}).listen(port,'127.0.0.1',()=>console.log(`RimFront OFFLINE DEMO: http://127.0.0.1:${port}/?demo=1`));
