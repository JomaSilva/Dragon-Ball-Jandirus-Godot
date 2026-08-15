using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS JANELAS DA BANCADA QUE FOTOGRAFA DOIS CORPOS (`--diagcorpos`) ============================
/// A `--doiscorposteste` mede os tres pedidos do dono em NUMERO, com o defeito injetado, e fecha 103 de
/// 103. E ela **passaria verde com o corpo desenhado atravessando o outro na tela**: entre a caixa dos
/// pes do servidor e o pixel ha o snapshot, o `World`, a interpolacao do corpo remoto e o Y-sort. Este
/// projeto ja catalogou esse cego quatro vezes ("a bancada mede INTENCAO"), e as tres fotos que o dono
/// pediu -- dois corpos colidindo, alguem carregado no ar, o cadaver antes e depois do enterro -- sao
/// exatamente a metade que so o olho fecha.
/// ========================================================================================================================
///
/// ============================ O QUE ESTAS JANELAS **NAO** FAZEM ============================
/// Nao escrevem `Pos`, nao escrevem `Altitude`, nao escrevem `AgarrandoId` e nao chamam `Prender`,
/// `LevarNoColo` nem `DesfazerOCadaver`. Quem agarra e o `AlternarAgarrao` (a tecla), quem voa e o
/// `AlternarVoo` (o verbo), quem anda e o `AplicarComando` (o atuador da IA) e quem enterra e o
/// `ComandoDeInteracao(pl, "enterrar")` (o botao do menu da tecla E). O cadaver nasce da MORTE de um
/// corpo, pelo `TickCombate`.
///
/// A unica coisa que elas escrevem e o que a bancada de qualquer jeito precisa por no mundo: um corpo
/// ao lado do jogador, e onde ele nasce.
/// ======================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// Faixa de ids propria -- longe do `_nextId` e das outras bancadas (91.200 da pose, 91.300 do alem,
	/// 91.700 da `--doiscorposteste`).
	/// </summary>
	private const int IdBaseDaFotoDeCorpos = 91_900;

	private int _fcCorpos;
	private readonly List<int> _fcNascidos = [];

	/// <summary>
	/// OS NOMES dos corpos que esta bancada forjou -- e a unica alca que a limpeza tem sobre os
	/// CADAVERES deles. O cadaver **nao herda a `Conta`** de quem morreu (ver `DeixarOCadaver`: a ficha
	/// dele nasce nova de proposito, pra nao carregar numero nenhum do morto), entao filtrar por conta
	/// aqui varreria zero corpos e deixaria os cadaveres de teste no mundo do dono. O que o cadaver
	/// carrega e o `NomeDeQuemMorreu`, e e por ele que a limpeza o acha.
	/// </summary>
	private readonly List<string> _fcNomes = [];

	/// <summary>
	/// UM CORPO AO LADO DO JOGADOR, com nome e aparencia -- e ele reusa o
	/// <see cref="ForjarCorpoDeFoto"/> da bancada da pose de proposito. Aquele metodo ja resolve as
	/// tres coisas que uma foto precisa e que sao faceis de esquecer: `PowerLevel` (senao o corpo nasce
	/// com poder expresso zero), `AparenciaDeNpc` (senao a foto sai com um marcador flutuando) e
	/// `TrocarAparencias` (senao sai um tufo de cabelo no chao, sem corpo e sem roupa).
	///
	/// O que muda aqui e so o REGISTRO: os corpos desta bancada saem na limpeza dela.
	/// </summary>
	internal int ForjarParaAFotoDeCorpos(int idDono, Vec2 desloc, string nome, double bp)
	{
		int id = ForjarCorpoDeFoto(idDono, desloc, nome, bp, comEscada: false);
		if (id != 0) { _fcNascidos.Add(id); _fcNomes.Add(nome); }
		_fcCorpos++;
		return id;
	}

	/// <summary>
	/// O QUE A FOTO PRECISA LER DE CADA CORPO. Nenhum destes campos e escrito por esta bancada.
	///
	/// A ALTITUDE E O ANDAR VEM JUNTOS porque a foto de "alguem sendo carregado no ar" so significa
	/// alguma coisa com os DOIS numeros ao lado da imagem: um corpo desenhado alto e um corpo desenhado
	/// no chao com a camera inclinada sao a mesma foto sem eles.
	/// </summary>
	internal (Vec2 Pos, float Altitude, int Andar, bool Voando, int Agarrando, int AgarradoPor,
			  bool Carregando, bool ECadaver, double Vida, int TiquesDeVoo)
		EstadoDoCorpoNaFoto(int id)
	{
		ServerPlayer? pl = CorpoDaFotoDeCorpos(id);
		if (pl == null) return (Vec2.Zero, 0, 0, false, 0, 0, false, false, 0, 0);
		return (pl.Pos, pl.Altitude, Voo.Andar(pl.Altitude), pl.Altitude > 0f,
				pl.AgarrandoId, pl.AgarradoPorId,
				pl.ModoDoAgarrao == ModoDeAgarrao.Carregando, pl.ECadaver, pl.Ficha.HP, pl.TiquesDeVoo);
	}

	/// <summary>
	/// PELA `ZoneList` E NAO SO PELO `_players` -- e a mesma razao do <see cref="CorpoNaMinhaZona"/>:
	/// o CADAVER nao esta no dicionario, e ele e o sujeito de duas das quatro fotos.
	/// </summary>
	private ServerPlayer? CorpoDaFotoDeCorpos(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) return pl;
		foreach (List<ServerPlayer> zona in _zones.Values)
			foreach (ServerPlayer o in zona)
				if (o.Id == id) return o;
		return null;
	}

	/// <summary>
	/// EMPURRA UM CORPO NUM RUMO -- <see cref="AplicarComando"/>, o atuador de producao, o mesmo por onde
	/// o `Rumo` que o `Cerebro.Pensar` devolve vira passo. E ele que consulta o `PodeMexerOCorpo` e o
	/// `MoveRules.Advance` com a <see cref="Vizinhanca"/> -- ou seja, e por ele que a colisao acontece.
	///
	/// Devolve quanto o corpo ANDOU DE FATO no tique. Zero com o comando dado e a assinatura de "bateu
	/// em alguma coisa", e e o numero que a legenda da foto de colisao carrega.
	/// </summary>
	internal float EmpurrarNaFotoDeCorpos(int id, Vec2 rumo, double dt)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		Vec2 antes = pl.Pos;
		AplicarComando(pl, new Jandirus.Core.Ai.Comando { Rumo = rumo }, dt);
		return (pl.Pos - antes).Length;
	}

	/// <summary>
	/// A TECLA DE AGARRAR -- <see cref="AlternarAgarrao"/>, o mesmo `case "agarrar"` do
	/// `C2S.Habilidade`. Um toque pega, dois carregam, tres soltam.
	/// </summary>
	internal void AgarrarNaFotoDeCorpos(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) AlternarAgarrao(pl);
	}

	/// <summary>PRA ONDE ESTE CORPO OLHA. E o que o `CorpoNaFrente` usa pra achar quem esta na frente.</summary>
	internal void ApontarNaFotoDeCorpos(int id, Facing olhar)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Facing = olhar;
	}

	/// <summary>
	/// A SKILL QUE ABRE O VOO, concedida -- a mesma coisa que o `--vooteste` faz com quem entra. O que
	/// esta bancada tem que atravessar e o `AlternarVoo` e a altitude, e nao a COMPRA da skill.
	/// </summary>
	internal void EnsinarAVoarNaFotoDeCorpos(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Livro.Dar(SkillDoKi);
		pl.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
	}

	/// <summary>O verbo `Fly`, e o bit continuo de subir -- os dois do jogador.</summary>
	internal void VoarNaFotoDeCorpos(int id, bool ligar)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (pl.Voando != ligar) AlternarVoo(pl);
	}

	/// <inheritdoc cref="VoarNaFotoDeCorpos"/>
	internal void SubirNaFotoDeCorpos(int id, bool querSubir)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.QuerSubir = querSubir;
	}

	/// <summary>
	/// ============================ MATA UM CORPO SEM DONO, E O CADAVER NASCE DA MORTE ============================
	/// `CombatState.Morrer` e o funil de producao (o mesmo do soco e do Kamehameha), e quem deixa o
	/// cadaver e o laco de NPC morto do <see cref="TickCombate"/> -- o `mobDeath.dm:15` do original, que
	/// chama o MESMO `GenerateCorpse()` do jogador.
	///
	/// **E O CORPO DO JOGADOR NAO E TOCADO.** Matar o host produziria o cadaver certo e tiraria a CAMERA
	/// da cena: ele viajaria pro Outro Mundo quinze segundos depois, e as fotos do enterro sairiam de um
	/// lugar onde ninguem esta. A `--doiscorposteste` mata o host porque mede a VIAGEM; esta fotografa o
	/// corpo que fica, e pra isso quem morre tem que ser o outro.
	/// ==========================================================================================================
	/// </summary>
	internal bool MatarNaFotoDeCorpos(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;

		// ============================ ELE PRECISA SER UM CORPO **DO MUNDO** PRA DEIXAR CADAVER ============================
		// Achado rodando: `Morrer` devolvia `true`, a morte acontecia, e cadaver nenhum aparecia. A
		// triagem (`VenceuOPrazoDaMorte`) tem TRES grupos e o cadaver so nasce em dois deles -- o
		// jogador (que viaja pro alem) e o **NPC do mundo** (que sai do mundo pelo `_npcsPraTirar`, e e
		// esse laco que chama o `GenerateCorpse`). Um corpo forjado sem `Peer` e sem `Papel` cai no
		// TERCEIRO grupo (clone, reflexo, boneco), que por desenho nao deixa corpo nenhum: o relogio da
		// morte dele vira `long.MaxValue` e ele fica ali.
		//
		// Dar um `Papel` a ele nao e um atalho, e o contrario: e o que faz este corpo ser exatamente o
		// que um habitante de planeta e -- e portanto morrer pelo MESMO caminho que o dono vai ver no
		// jogo. Sem isto, esta bancada fotografaria um cadaver que so a bancada sabe criar.
		// =============================================================================================================
		pl.Papel ??= new Jandirus.Core.Npc.PapelDeNpc(
			new Jandirus.Core.Npc.MoldeDeNpc { Id = "bancada_foto", Racas = ["Saiyan"], Classe = "Saiyan" },
			(ulong)id);

		return pl.Combate.Morrer(ignorarSeguro: true);
	}

	/// <summary>O CADAVER AO ALCANCE deste jogador, ou 0 -- a MESMA busca que acende o "[E] corpo de Fulano".</summary>
	internal (int Id, string Nome) CadaverPertoNaFotoDeCorpos(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (0, "");
		ServerPlayer? c = CadaverPerto(pl);
		return (c?.Id ?? 0, c?.Name ?? "");
	}

	/// <summary>
	/// A LAPIDE mais recente desta zona, se houver -- pra a legenda da foto do DEPOIS dizer o que esta
	/// desenhado ali. Ela e uma <see cref="Obra"/> de verdade, e ja foi pro `mundo.json`.
	/// </summary>
	internal (bool Achou, string Tipo, string Epitafio, Vec2 Onde) LapideNaFotoDeCorpos(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (false, "", "", Vec2.Zero);
		Obra? l = null;
		foreach (Obra o in _noChao)
			if (o.Zona.Equals(pl.Zone) && o.Tipo.StartsWith("Grave_", StringComparison.Ordinal)) l = o;
		return l == null ? (false, "", "", Vec2.Zero) : (true, l.Tipo, l.Epitafio, new Vec2(l.X, l.Y));
	}

	/// <summary>
	/// ============================ ONDE CABE UMA CENA DE DOIS CORPOS ANDANDO ============================
	/// A mesma pergunta -- e a mesma funcao -- da `--doiscorposteste`: o passo consulta
	/// <see cref="MoveRules.Occupied"/> com <see cref="ModoDeTravessia.APe"/>, entao a reta tem que ser
	/// livre PRA QUEM ANDA (parede **e agua**). Aquela bancada nasceu perguntando so por parede e
	/// reprovou tres linhas em Namek com o corpo parado na beira de um lago.
	/// </summary>
	/// <remarks>
	/// ============================ E ELA PREFERE O EIXO **HORIZONTAL**, POR CAUSA DA FOTO ============================
	/// A caixa dos pes e 16x10 px (`MoveRules.BodyHalfW/H`), e ela e achatada de proposito -- chao visto
	/// de cima em perspectiva e achatado, e o proprio `Corpos.cs` explica por que engorda-la seria pior.
	/// A consequencia pra a IMAGEM, medida na primeira rodada: dois corpos que se encontram pelo eixo Y
	/// param a **12 px** um do outro, e dois sprites de 32 px a 12 px de distancia se leem como UM SO.
	/// A foto ficava tecnicamente certa e dizia o contrario do que prova.
	///
	/// Pelo eixo X eles param a ~16 px, e os dois bonecos aparecem lado a lado. **Isto e uma escolha de
	/// ENQUADRAMENTO e nao de regra** -- a `--doiscorposteste` mede os dois eixos pelo rumo que o mapa
	/// oferecer, e o numero e o mesmo. Aqui so se escolhe de que angulo fotografar.
	/// ============================================================================================================
	/// </remarks>
	internal (bool Achou, Vec2 Origem, Facing Rumo) PalcoDaFotoDeCorpos(int id, int tiles)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (false, Vec2.Zero, Facing.South);
		Facing[] ordem = [Facing.East, Facing.West, Facing.South, Facing.North];
		if (AcharPalco(pl, tiles, ordem) is not { } p) return (false, Vec2.Zero, Facing.South);
		return (true, p.Origem, p.Rumo);
	}

	/// <summary>
	/// TIRA DO MUNDO tudo o que esta bancada pos nele -- os corpos forjados, os cadaveres que eles
	/// deixaram, e as LAPIDES.
	///
	/// A lapide e a unica coisa desta bancada que vai pro DISCO (`mundo.json`), entao ela e a unica que
	/// sujaria a partida do dono -- um tumulo de teste por rodada, pra sempre. Mesma disciplina do
	/// `finally` da `--doiscorposteste`.
	/// </summary>
	internal void LimparAFotoDeCorpos(int obrasAntes)
	{
		foreach (int id in _fcNascidos)
		{
			ServerPlayer? pl = CorpoDaFotoDeCorpos(id);
			if (pl == null) continue;
			if (pl.AgarrandoId != 0) Soltar(pl, MotivoDaSoltura.Tecla);
			if (pl.AgarradoPorId != 0) LimparPreso(pl);
			_npcsPraTirar.Remove(pl);
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		_fcNascidos.Clear();

		// OS CADAVERES que os corpos forjados deixaram. Eles nao estao no `_players` (por desenho), e
		// por isso nao saem pela varredura acima.
		var cadaveres = new List<ServerPlayer>();
		foreach (List<ServerPlayer> zona in _zones.Values)
			foreach (ServerPlayer o in zona)
				if (o.ECadaver && _fcNomes.Contains(o.NomeDeQuemMorreu)) cadaveres.Add(o);
		foreach (ServerPlayer c in cadaveres) DesfazerOCadaver(c);
		_fcNomes.Clear();

		if (obrasAntes >= 0 && _noChao.Count > obrasAntes)
		{
			ZoneKey zona = _noChao[^1].Zona;
			_noChao.RemoveRange(obrasAntes, _noChao.Count - obrasAntes);
			GravarMundo();
			MandarObras(zona);
		}
	}

	/// <summary>Quantas construcoes existem agora -- a marca que o <see cref="LimparAFotoDeCorpos"/> usa.</summary>
	internal int ObrasAgoraNaFotoDeCorpos() => _noChao.Count;
}
