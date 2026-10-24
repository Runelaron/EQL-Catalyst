# Pet inventory and item evidence

Pet inventory keeps its recorded ranks and position effects. Search, Expansion and Filter act on candidates; moving a preview slider never changes an inventory item. **Reset preview** restores Set Rank when explicit, otherwise the selected recorded copy, otherwise the catalog +0 scenario. Reset stays disabled at that baseline. Actual records above +10 remain intact.

Hover an inventory name for its effective slot, ratio and evidence. **Exalt** lists populated Focus, Click, Worn and Proc effects in that order. Intrinsic effects remain item facts; configured effects remain attached to numbered positions. Locked, planned and inactive effects are identified separately. Pet Details holds summon/focus context; compact gear totals exclude unknown pet base statistics.

## Acquisition evidence

The bundled supplement records sourced drops, purchases and confirmed quest outputs. A related-quest link is a lead, not proof that the item is a reward: the parser checks a reward field, Rewards section, named Gear Set, explicit walkthrough output or the first reward column of a Plane of Sky test row. Required components are not converted into rewards. Quest starting locations remain undocumented when no supported start location exists.

Run the resumable public-data audit without accessing game files or profiles:

```powershell
python scripts/audit-item-evidence.py --output artifacts/catalog-audit
python scripts/audit-item-evidence.py --output artifacts/catalog-audit --related
dotnet run --project scripts/CatalogAudit -c Release -- . artifacts/catalog-audit/wiki-pages.json artifacts/catalog-audit
```

Review generated corrections before copying acquisition-evidence.json into data. The audit reports every bundled item and indexed title, including unresolved relationships and indexed-only pages; an indexed title does not by itself establish equipment or acquisition.

EQLWiki-derived factual records retain source URLs and revision identifiers under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). Relevant references include [Plane of Sky](https://eqlwiki.com/Plane_of_Sky), [Belt of the Four Winds](https://eqlwiki.com/Belt_of_the_Four_Winds), and [Golden Sash of Tranquility](https://eqlwiki.com/Golden_Sash_of_Tranquility). The supplement is adapted factual data, not a relicensing of the wiki.

## Ranked statistics

Catalog baseline rank, a physical copy's recorded rank and fractional tier progress are different facts. Owning an item does not confirm its upgraded statistics. Ordinary positive-stat projections follow the documented cumulative 10%, round-down and completed-tier minimum rules. Explicit fractional progress can change the percentage without awarding the next tier's minimum. Delay remains fixed. Missing inputs stay missing.

The adopted weapon model is floor(base damage × (1 + 0.10 × (rank + known fractional progress))), without the ordinary-stat minimum. Delay stays fixed. Bloodmoon +4 therefore projects 25/28; Wu's Fist +1, +4 and +10 project 17/22, 22/22 and 32/22. This is a working calculation model, not a claim that conflicting published guidance has been resolved. Exact client observations override only their recorded item/rank/conditions: Whip +4 is 7/14 and Evensong +5 is 16/25. Damage bonus is not added to ratio.

The model produces numeric equipment preference ratings when required inputs are present. Genuine missing metadata, eligibility and uncertain hand-selection mechanics remain separate limitations. Haste and negative stats retain catalog values unless observed; unresolved weighted fields can still require an explanation. Weight does not enter these ratings: WT (base) identifies catalog weight when no exact ranked observation is available. Formula disagreement and physical-weight caveats belong in information details, not blanket rank warnings.

## Cross-check limitations

[EQL Tools' Sky tracker](https://eqltools.com/sky) identifies EQLWiki as its quest-data source, so agreement is corroboration, not independent confirmation. Its upgrade guide explicitly preserves the damage-scaling disagreement. The [eqlegends.wiki Exaltation guide](https://eqlegends.wiki/guide/exaltation/) offers general guidance rather than item/rank observations; its all-effects-at-+4 wording does not independently establish individual unlock mechanics. Unsupported statements do not override current sources or exact observations. The [official October 13 notes](https://www.everquestlegends.com/patch-notes/eql-update-notes-10-13-2026) establish increased ratio importance, not a complete pet damage formula.
