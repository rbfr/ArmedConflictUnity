Kenney Nature Kit (2.1) — builder inputs only.

Source: https://kenney.nl/assets/nature-kit
License: CC0 (see License.txt). Credit Kenney / www.kenney.nl (not required).

These GLBs are NOT Unity scene assets. SpikeSceneBattle wires every
file in Assets/Models, so a kit dump there would mint unused slots.
build_backdrop_forest.py imports them, splits trunk/canopy by
material name (woodBark* / leafs*), retints via body/trim_/accent_,
and bakes the stand into backdrop_forest_{near,mid,far}.glb.
