"""Refresh only explicitly labeled quest-start fields from public EQLWiki revisions."""
import argparse,json,re,time,urllib.request,urllib.parse
from pathlib import Path

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--catalog',type=Path,default=Path('data/catalog.json'));parser.add_argument('--output',type=Path,default=Path('data/quest-starts.json'));args=parser.parse_args()
    catalog=json.loads(args.catalog.read_text(encoding='utf-8-sig'));titles=[q.get('Name',q.get('name')) for q in catalog.get('Quests',catalog.get('quests',[]))];facts=[]
    def field(text,label):
        m=re.search(r"^![ \t']*(?:"+label+r")\s*:?\s*'*\s*\r?\n\|\s*([^\r\n]+)",text,re.M|re.I)
        if not m:m=re.search(r'^\|\s*(?:'+label.replace(' ','[ _]?')+r')\s*=\s*([^\r\n]+)',text,re.M|re.I)
        if not m:return ''
        return re.sub(r"<[^>]*>|'{2,}",'',re.sub(r'\[\[([^]|]+)(?:\|([^]]+))?\]\]',lambda x:x[2] or x[1],m[1])).strip()
    for start in range(0,len(titles),40):
        url='https://eqlwiki.com/api.php?'+urllib.parse.urlencode(dict(action='query',prop='revisions',rvprop='ids|content',rvslots='main',format='json',titles='|'.join(titles[start:start+40])))
        with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'EQLTracker/1.0 (local companion; cached batched requests)'}),timeout=45) as response:data=json.load(response)
        for page in data.get('query',{}).get('pages',{}).values():
            if not page.get('revisions'):continue
            revision=page['revisions'][0];text=revision['slots']['main']['*'];zone=field(text,'Start Zone|Starting Zone');npc=field(text,'Quest Giver|Start NPC|Starting NPC')
            if zone or npc:facts.append(dict(Name=page['title'],Revision=revision['revid'],StartZone=zone,StartNpc=npc,Source='https://eqlwiki.com/'+urllib.parse.quote(page['title'].replace(' ','_'))+'?oldid='+str(revision['revid'])+' · structured quest-start fields · parser v1'))
        time.sleep(.2)
    args.output.write_text(json.dumps(sorted(facts,key=lambda f:f['Name']),indent=2,ensure_ascii=False)+'\n',encoding='utf-8');print(json.dumps(dict(QuestPages=len(titles),ExplicitStarts=len(facts))))
if __name__=='__main__':main()
