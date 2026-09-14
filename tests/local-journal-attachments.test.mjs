import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import test from 'node:test';
const source = await readFile(new URL('../BlazorApp1/wwwroot/js/local-journal-attachments.js', import.meta.url), 'utf8');
const {validateFiles, entryDirectory, entrySegments, uniqueName} = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const context = {companyFolder:'proacc-0123456789abcdef',entryFolder:'2026-13339'};
class Directory {
    constructor() { this.children = new Map(); }
    async getDirectoryHandle(name, {create}) {
        if (!this.children.has(name)) {
            if (!create) throw new DOMException('Missing', 'NotFoundError');
            this.children.set(name, new Directory());
        }
        return this.children.get(name);
    }
}
test('listing missing attachments does not create folders', async()=> {
    const root=new Directory();
    assert.equal(await entryDirectory(root,context),null);
    assert.equal(root.children.size,0);
});
test('company and period isolate attachments with identical entry IDs', async()=> {
    const root=new Directory();
    const first=await entryDirectory(root,context,true);
    assert.equal(await entryDirectory(root,context),first);
    assert.notEqual(await entryDirectory(root,{...context,companyFolder:'proacc-aaaaaaaaaaaaaaaa'},true),first);
    assert.notEqual(await entryDirectory(root,{...context,entryFolder:'2025-13339'},true),first);
});
test('rejects traversal in folder metadata and filenames',()=> {
    for(const entryFolder of ['../13339','2026/13339','2026-../x']) assert.throws(()=>entrySegments({...context,entryFolder}));
    for(const name of ['../a.txt','x\\a.txt','..','bad\0.txt']) assert.throws(()=>validateFiles([{name,size:1}]));
});
test('enforces complete batch limits before any file is written',()=> {
    assert.doesNotThrow(()=>validateFiles([{name:'مرفق.pdf',size:25*1024*1024}]));
    assert.throws(()=>validateFiles([{name:'too-big.pdf',size:25*1024*1024+1}]));
    assert.throws(()=>validateFiles(Array.from({length:21},()=>({name:'a.txt',size:1}))));
});
test('duplicate names produce distinct files and retain extensions',()=> {
    const a=uniqueName('مرفق.pdf'),b=uniqueName('مرفق.pdf');
    assert.notEqual(a,b); assert.ok(a.endsWith('.pdf')); assert.ok(b.endsWith('.pdf'));
});
class Element {
    constructor(tag) { this.tag=tag; this.children=[]; this.handlers={}; this.classList={toggle(){}}; this.textContent=''; }
    append(...items){this.children.push(...items);}
    replaceChildren(...items){this.children=items;}
    setAttribute(){}
    addEventListener(event,handler){this.handlers[event]=handler;}
    querySelectorAll(tag){return this.children.flatMap(x=>[...(x.tag===tag?[x]:[]),...x.querySelectorAll(tag)]);}
    click(){this.handlers.click?.();}
}
test('native picker runs before authorization and file bytes stay in the local directory',async()=> {
    const {mount,unmount}=await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
    const saved=new Map(), calls=[];
    const leaf={async *entries(){for(const name of saved.keys())yield[name,{kind:'file'}];}, async getFileHandle(name){return {async createWritable(){return new WritableStream({write(chunk){saved.set(name,chunk);}});}};},async removeEntry(name){saved.delete(name);}};
    const middle={async getDirectoryHandle(){return leaf;}};
    const root={name:'Attach',async getDirectoryHandle(){return middle;}};
    globalThis.document={createElement:tag=>new Element(tag)};
    globalThis.window={isSecureContext:true,showDirectoryPicker(){calls.push('picker');return Promise.resolve(root);}};
    const callback={async invokeMethodAsync(method,...args){assert.deepEqual(args,[]);assert.equal(method,'AuthorizeLocalOperation');calls.push('authorize');return {...context,scope:'scope',configuredPath:'C:\\Attach'};}};
    const host=new Element('div');
    mount(host,{...context,scope:'scope',configuredPath:'C:\\Attach'},callback);
    host.querySelectorAll('button')[0].click();
    assert.deepEqual(calls,['picker']);
    for(let i=0;i<10;i++)await new Promise(setImmediate);
    const input=host.querySelectorAll('input')[0];
    assert.equal(input.disabled,false);
    input.files=[new File(['local file content'],'test.txt')];
    input.handlers.change();
    for(let i=0;i<20;i++)await new Promise(setImmediate);
    assert.equal(saved.size,1);
    assert.equal(new TextDecoder().decode([...saved.values()][0]),'local file content');
    assert.equal(calls.filter(x=>x==='authorize').length,2);
    unmount(host); assert.equal(host.children.length,0);
});
