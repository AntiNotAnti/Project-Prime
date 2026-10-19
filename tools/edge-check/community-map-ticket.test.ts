import { strict as assert } from "node:assert";
import { test } from "node:test";
import { readFileSync } from "node:fs";
let handler: (request: Request) => Promise<Response>;
const subject="11111111-1111-4111-8111-111111111111";
let calls=0, failAuth=false, invalidUser=false, service: string|undefined="closed-test-service";
(globalThis as any).Deno={env:{get:(name:string)=>({SUPABASE_URL:"http://closed-auth.invalid",SUPABASE_SERVICE_ROLE_KEY:service} as Record<string,string|undefined>)[name]},serve:(fn:typeof handler)=>handler=fn};
globalThis.fetch=async (url:RequestInfo|URL,init?:RequestInit)=>{
 assert.equal(String(url),"http://closed-auth.invalid/auth/v1/user","all external HTTP forbidden");calls++;
 assert.equal(new Headers(init?.headers).get("apikey"),service);
 if(failAuth)throw new Error("private credential user@example.test not for client");
 if(new Headers(init?.headers).get("authorization")!=="Bearer closed-user-token")return new Response("{}",{status:401});
 return new Response(JSON.stringify({id:invalidUser?"not-a-user-id":subject}));
};
await import("../../supabase/functions/community-map-ticket/index.ts");
function request(body:unknown,auth="Bearer closed-user-token"){return new Request("http://closed-handler.invalid",{method:"POST",headers:auth?{authorization:auth}:{},body:JSON.stringify(body)});}
function reset(){calls=0;failAuth=false;invalidUser=false;service="closed-test-service";}
const json=(response:Response)=>response.json() as Promise<any>;
function text(bytes:Uint8Array){return Buffer.from(bytes).toString("base64url");}
async function signed(payload:unknown,signatureService="closed-test-service"){
 const p=text(new TextEncoder().encode(JSON.stringify(payload)));
 const key=await crypto.subtle.importKey("raw",new TextEncoder().encode("project-prime-community-map-ticket:v1:"+signatureService),{name:"HMAC",hash:"SHA-256"},false,["sign"]);
 return "ppm1."+p+"."+text(new Uint8Array(await crypto.subtle.sign("HMAC",key,new TextEncoder().encode(p))));
}
test("method and bounded object parsing reject before Auth or ticket work",async()=>{
 reset();assert.equal((await handler(new Request("http://closed.invalid"))).status,405);
 for(const body of ["null","[]","true","{","5",'"scalar"',""]){
  const response=await handler(new Request("http://closed.invalid",{method:"POST",body}));assert.equal(response.status,400);
 }
 assert.equal((await handler(request({padding:"x".repeat(9000)}))).status,413);
 assert.equal((await handler(new Request("http://closed.invalid",{method:"POST",headers:{"content-length":"9000"},body:"{}"}))).status,413);
 assert.equal(calls,0);
});
test("exact body ceiling remains usable without widening admitted action",async()=>{
 reset();const prefix=JSON.stringify({padding:""});const exact=JSON.stringify({padding:"x".repeat(8192-Buffer.byteLength(prefix))});assert.equal(Buffer.byteLength(exact),8192);
 assert.equal((await handler(new Request("http://closed.invalid",{method:"POST",headers:{authorization:"Bearer closed-user-token"},body:exact}))).status,200);assert.equal(calls,1);
});
test("streaming cap cancels overflow and invalid UTF8 is refused before Auth",async()=>{
 reset();let canceled=false;
 const body=new ReadableStream<Uint8Array>({start(c){c.enqueue(new Uint8Array(5000));c.enqueue(new Uint8Array(5000));},cancel(){canceled=true;}});
 assert.equal((await handler(new Request("http://closed.invalid",{method:"POST",body,duplex:"half"} as any))).status,413);assert.equal(canceled,true);
 assert.equal((await handler(new Request("http://closed.invalid",{method:"POST",body:new Uint8Array([0xff])}))).status,400);assert.equal(calls,0);
});
test("mint requires exact Bearer and successful current Auth user",async()=>{
 reset();for(const auth of ["","Basic token","Bearer","Bearer bad token","Bearer wrong"]){assert.equal((await handler(request({},auth))).status,401);}
 invalidUser=true;assert.equal((await handler(request({action:"mint"}))).status,401);
 failAuth=true;const failure=await handler(request({}));assert.equal(failure.status,503);assert.equal((await failure.text()).includes("private credential"),false);
});
test("actual HMAC mint/verify preserves subject, short expiry and nonmoderator identity",async()=>{
 reset();const before=Math.floor(Date.now()/1000);const minted=await json(await handler(request({action:"mint",sub:"spoof",creator_id:"spoof",moderator:true})));assert.equal(calls,1);
 assert.match(minted.ticket,/^ppm1\.[\w-]+\.[\w-]+$/);const payload=JSON.parse(Buffer.from(minted.ticket.split('.')[1],"base64url").toString());
 assert.equal(payload.sub,subject);assert.equal(payload.exp-payload.iat,900);assert.ok(payload.iat>=before);assert.equal(minted.expires_at,new Date(payload.exp*1000).toISOString());
 const verified=await handler(request({action:"verify",ticket:minted.ticket},""));assert.equal(verified.status,200);assert.deepEqual(await json(verified),{creator_id:"hunter:"+subject,moderator:false,expires_at:minted.expires_at});assert.equal(calls,1);
});
test("default mint action remains compatible and unknown actions refuse",async()=>{
 reset();assert.equal((await handler(request({}))).status,200);assert.equal((await handler(request({action:"admin"}))).status,400);assert.equal(calls,1);
});
test("invalid signed shapes and temporal bounds are authentication refusals",async()=>{
 reset();const now=Math.floor(Date.now()/1000);const normal={v:1,sub:subject,iat:now,exp:now+900};
 for(const payload of [null,[],{}, {...normal,v:2},{...normal,sub:"bad"},{...normal,iat:now+61},{...normal,exp:now},{...normal,exp:now+961},{...normal,iat:now+30,exp:now+20},{...normal,iat:1.5}]){
  const response=await handler(request({action:"verify",ticket:await signed(payload)},""));assert.equal(response.status,401,JSON.stringify(payload));
 }
 assert.equal(calls,0);
});
test("forged malformed oversized and wrong-service tickets cannot verify",async()=>{
 reset();const now=Math.floor(Date.now()/1000);const other=await signed({v:1,sub:subject,iat:now,exp:now+900},"different-service");
 for(const ticket of ["",null,4,"x".repeat(4097),"ppm1.bad.bad","other.bad.bad",other])assert.equal((await handler(request({action:"verify",ticket},""))).status,401);
 assert.equal(calls,0);
});
test("missing signing service fails generically without private exception leakage",async()=>{
 reset();const now=Math.floor(Date.now()/1000);const ticket=await signed({v:1,sub:subject,iat:now,exp:now+900});service=undefined;
 for(const body of [{action:"verify",ticket},{action:"mint"}]){const response=await handler(request(body));assert.equal(response.status,503);const error=await response.text();assert.equal(error.includes("closed-test-service"),false);assert.equal(error.includes("Supabase service role"),false);}
 reset();
});
test("dependency metadata uses existing exact functions runtime identity with no floating SDK",()=>{
 const source=readFileSync(new URL("../../supabase/functions/community-map-ticket/index.ts",import.meta.url),"utf8");
 assert.ok(source.includes("npm:@supabase/functions-js@2.117.2/src/edge-runtime.d.ts"));assert.equal(source.includes("supabase-js@2"),false);
 const lock=JSON.parse(readFileSync(new URL("../../supabase/functions/community-map-ticket/deno.lock",import.meta.url),"utf8"));
 assert.equal(lock.specifiers["npm:@supabase/functions-js@2.117.2"],"2.117.2");assert.equal(Object.keys(lock.specifiers).length,1);assert.ok(lock.npm["@supabase/functions-js@2.117.2"].integrity.startsWith("sha512-"));
});
