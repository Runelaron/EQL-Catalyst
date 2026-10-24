"""Read-only, resumable public-wiki audit. Writes evidence outside application profiles."""
import argparse,json,time,urllib.request,urllib.parse,re
from pathlib import Path

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',required=True);parser.add_argument('--related',action='store_true');args=parser.parse_args()
    root=Path(__file__).resolve().parents[1];out=Path(args.output);out.mkdir(parents=True,exist_ok=True)
    catalog=json.loads((root/'data/catalog.json').read_text());equipment=json.loads((root/'data/equipment.json').read_text())
    items={i['key']:i for i in catalog['items']+equipment['items']}
    titles=sorted({urllib.parse.unquote(i.get('wikiUrl','').split('/')[-1]).replace('_',' ') if i.get('wikiUrl') else i['name'] for i in items.values()})
    cache=out/'wiki-pages.json';saved=json.loads(cache.read_text()) if cache.exists() else {'pages':{},'redirects':{},'missing':[]}
    if args.related:
        titles=sorted({link.split('#')[0].replace('_',' ').strip() for page in saved['pages'].values() for field in re.findall(r'\|relatedquests\s*=(.*?)(?=\n\|[\w ]+\s*=|\}\})',page['text'],re.S|re.I) for link in re.findall(r'\[\[([^\]|]+)',field)})
    pending=[t for t in titles if t not in saved['pages'] and t not in saved['redirects'] and t not in saved['missing']]
    for offset in range(0,len(pending),50):
        batch=pending[offset:offset+50]
        url='https://eqlwiki.com/api.php?'+urllib.parse.urlencode(dict(action='query',prop='revisions',rvprop='ids|content|timestamp',rvslots='main',redirects=1,format='json',titles='|'.join(batch),maxlag=5))
        for attempt in range(4):
            try:
                request=urllib.request.Request(url,headers={'User-Agent':'EQL-Catalyst/1.4 catalog evidence audit (read-only)'})
                with urllib.request.urlopen(request,timeout=45) as response:data=json.load(response)
                if 'error' in data:raise RuntimeError(data['error'])
                break
            except Exception:
                if attempt==3:raise
                time.sleep(2*(attempt+1))
        query=data.get('query',{})
        for row in query.get('normalized',[])+query.get('redirects',[]):saved['redirects'][row['from']]=row
        for page in query.get('pages',{}).values():
            if 'revisions' in page:
                rev=page['revisions'][0];saved['pages'][page['title']]={'revision':rev['revid'],'timestamp':rev['timestamp'],'text':rev['slots']['main']['*']}
            else:saved['missing'].append(page['title'])
        temporary=cache.with_suffix('.tmp');temporary.write_text(json.dumps(saved,ensure_ascii=False),encoding='utf-8');temporary.replace(cache)
        print(json.dumps({'Fetched':min(offset+len(batch),len(pending)),'Requested':len(pending),'CachedPages':len(saved['pages'])}),flush=True);time.sleep(1)
    def field(i,name):return i.get(name,i.get(name[0].upper()+name[1:],[]))
    rows=[{'key':key,'name':i['name'],'sourceCount':len(field(i,'sources')),'status':'Has bundled acquisition records' if field(i,'sources') else 'Requires acquisition review'} for key,i in items.items()]
    aliases={i['name'].casefold() for i in items.values()}|{a.casefold() for i in items.values() for a in i.get('aliases',[])}
    indexed=[{'title':t,'status':'Bundled definition or alias' if t.casefold() in aliases else 'Indexed only; on-demand metadata, not a confirmed equipment definition'} for t in equipment['Titles']]
    (out/'coverage-before.json').write_text(json.dumps({'Items':rows,'IndexedTitles':indexed,'UniqueBundledItems':len(items),'IndexedTitleCount':len(indexed),'WithoutSources':sum(r['sourceCount']==0 for r in rows),'CachedPages':len(saved['pages']),'MissingPages':saved['missing']},indent=2),encoding='utf-8')
if __name__=='__main__':main()
