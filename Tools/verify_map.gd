extends SceneTree

func _init():
	for nome in ["z01_Earth", "z02_Namek", "z06_Afterlife"]:
		var cena = load("res://Assets/Maps/%s.tscn" % nome)
		if cena == null:
			print("FALHOU ao carregar ", nome); continue
		var n = cena.instantiate()
		var layer = n.get_node("Chao")
		var cells = layer.get_used_cells()
		print("%s: %d celulas | rect %s" % [nome, cells.size(), str(layer.get_used_rect())])
		if cells.size() > 0:
			var c = cells[0]
			print("   amostra ", c, " fonte=", layer.get_cell_source_id(c), " atlas=", layer.get_cell_atlas_coords(c))
	quit()
