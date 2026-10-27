#!/usr/bin/env node
import {createServer} from 'node:http';
import {ImportStore} from './import-store.js';
import {createHandler} from './import-http.js';
try{
 const root=process.env.WATERMELONFIN_IMPORT_ROOT;if(!root)throw new Error('Missing root');
 const store=new ImportStore(root);await store.init();
 const handler=createHandler(store,process.env.WATERMELONFIN_IMPORT_SECRET);
 const server=createServer(handler);server.requestTimeout=120000;server.headersTimeout=10000;server.maxRequestsPerSocket=100;
 server.on('error',()=>{console.error('Importer failed to bind.');process.exitCode=1;});
 server.listen(Number(process.env.WATERMELONFIN_IMPORT_PORT??9784),'127.0.0.1',()=>console.error('WatermelonFin importer listening on loopback. Use a trusted private HTTPS proxy for remote access.'));
}catch{console.error('Importer startup failed; check dedicated root and secret configuration.');process.exitCode=1;}
