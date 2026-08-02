extends SceneTree
func _init() -> void:
	var ts: TileSet = load("res://Assets/Maps/tileset.tres")
	var oc := 0; var grandes := 0; var total := 0
	for i in range(ts.get_source_count()):
		var src := ts.get_source(ts.get_source_id(i)) as TileSetAtlasSource
		if src == null: continue
		var r := src.texture_region_size
		if r.x != 32 or r.y != 32: grandes += src.get_tiles_count()
		for k in range(src.get_tiles_count()):
			total += 1
			var td := src.get_tile_data(src.get_tile_id(k), 0)
			if td != null and td.get_occluder_polygons_count(0) > 0: oc += 1
	print("tiles %d | COM OCLUSOR %d | de regiao maior que 32x32: %d" % [total, oc, grandes])
	quit(0)
