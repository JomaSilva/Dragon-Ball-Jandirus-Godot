using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// (v0.2.1, lote T4 da tech) O TELEPORTE E A VIGILANCIA -- o telepad, o relogio de teleporte e o anulador; as
/// cameras, o computador de cameras, as frequencias e a biblioteca do PDA, e o comunicador. As regras moram em
/// `Core/Tech/Comunicacoes.cs` (`Teleporte`, `Vigilancia`), com o que o original faz e o que muda aqui.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Os verbos de maquina deste lote (o menu da tecla E). Falso quando o verbo nao e daqui.</summary>
	private bool ComandoDeComunicacao(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case Teleporte.VerboCanalDoTelepad: DefinirOCanalDoTelepad(pl, arg); return true;
			case Teleporte.VerboReforcarOTelepad: ReforcarOTelepad(pl); return true;
			case Vigilancia.VerboFrequencia: DefinirAFrequenciaDaCamera(pl, arg); return true;
			case Vigilancia.VerboMaisFrequencia: MexerNasFrequencias(pl, arg, tirar: false); return true;
			case Vigilancia.VerboMenosFrequencia: MexerNasFrequencias(pl, arg, tirar: true); return true;
			case Vigilancia.VerboFrequencias: DizerAsFrequencias(pl); return true;
			case Vigilancia.VerboEnviar: EnviarPeloComunicador(pl, arg); return true;
			case Vigilancia.VerboMegafone: GritarNoMegafone(pl, arg); return true;
			case Vigilancia.VerboMelhorar: MelhorarOComunicador(pl, arg); return true;
			case Vigilancia.VerboCameras: VerAsCamerasDoComunicador(pl); return true;
			case Vigilancia.VerboHistorico: VerOHistorico(pl); return true;
			case Vigilancia.VerboDna: TrancarOComunicador(pl); return true;
			// (lote T5) o sintetizador de virus -- `GameServer.Sintetizador.cs`.
			default: return ComandoDeSintetizador(pl, cmd, arg);
		}
	}

	/// <summary>As acoes de mochila deste lote. Falso quando a acao nao e daqui.</summary>
	private bool AcaoDeComunicacao(ServerPlayer pl, ItemDef def, string acao, string arg2)
	{
		switch (acao)
		{
			case Teleporte.AcaoCanal: DefinirOCanalDoRelogio(pl, def, arg2); return true;
			case Teleporte.AcaoDestinos: ListarOsDestinos(pl, def); return true;
			case Teleporte.AcaoIr: ViajarPeloRelogio(pl, def, arg2); return true;
			case Teleporte.AcaoFalar: FalarPeloRelogio(pl, def, arg2); return true;
			case Teleporte.AcaoHenshin: Henshin(pl); return true;
			case Teleporte.AcaoDna: TrancarORelogio(pl, def); return true;
			case Teleporte.AcaoAnular: LigarOAnulador(pl); return true;
			case Vigilancia.AcaoFrequencia: DefinirAFrequenciaDoComputador(pl, def, arg2); return true;
			case Vigilancia.AcaoMaisFrequencia: MexerNasFrequenciasDoPda(pl, def, arg2, tirar: false); return true;
			case Vigilancia.AcaoMenosFrequencia: MexerNasFrequenciasDoPda(pl, def, arg2, tirar: true); return true;
			case Vigilancia.AcaoVer: VerAsCamerasDoItem(pl, def); return true;
			case Vigilancia.AcaoDna: TrancarOItemDeCameras(pl, def); return true;
			case Vigilancia.AcaoPublicar: PublicarNaBiblioteca(pl, arg2); return true;
			case Vigilancia.AcaoBiblioteca: ListarABiblioteca(pl); return true;
			case Vigilancia.AcaoImprimir: ImprimirDaBiblioteca(pl, arg2); return true;
			// (lote T5) os companheiros -- `GameServer.Companheiros.cs`.
			default: return AcaoDeCompanheiro(pl, def, acao, arg2);
		}
	}

	// =====================================================================
	// O TELEPAD -- `Teleports.dm:172-207`. A regra esta em `Teleporte`.
	// =====================================================================
	/// <summary>`verb/Set()`: `telechannel = input(usr,"Set the teleport channel.") as text` -- de quem estiver do lado, como la.</summary>
	private void DefinirOCanalDoTelepad(ServerPlayer pl, string cru)
	{
		Obra? o = ObraQueAceita(pl, Teleporte.VerboCanalDoTelepad);
		if (o == null) { Avisar(pl, "não há telepad por perto."); return; }
		string canal = TechDeBolso.Limpo(cru, Teleporte.MaxDeCanal);
		if (canal.Length == 0) { Avisar(pl, "o canal não pode ser vazio."); return; }
		(o.Tech ??= new EstadoDeTech()).S[Teleporte.ChaveCanal] = canal;
		GravarMundo();
		Avisar(pl, $"canal do telepad: \"{canal}\". Os relógios desse canal chegam aqui" + (o.Aparafusada ? "." : " -- quando ele estiver aparafusado."));
	}

	/// <summary>O `Click()` do telepad: `maxarmor = usr.intBPcap; healDamage(maxarmor)`. Como no reforcador, so SOBE.</summary>
	private void ReforcarOTelepad(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, Teleporte.VerboReforcarOTelepad);
		if (o == null) { Avisar(pl, "não há telepad por perto."); return; }
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		double teto = TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod);
		if (o.ArmaduraMax < teto) o.ArmaduraMax = teto;
		o.Armadura = o.ArmaduraMax;
		GravarMundo();
		Avisar(pl, "telepad reforçado!");   // `"Telepad upgraded!"`
	}

	// =====================================================================
	// O RELOGIO DE TELEPORTE -- `Teleports.dm:64-169`
	// =====================================================================
	/// <summary>
	/// Um destino do relogio: o nome que a lista mostra, e QUEM ele e -- um telepad (o id da obra) ou outro relogio (o
	/// id de quem o carrega). Onde ele ESTA so se pergunta na chegada (<see cref="OndeEstaODestino"/>).
	/// </summary>
	private readonly record struct DestinoDoRelogio(string Nome, int Telepad, int Portador);

	/// <summary>As viagens em curso (os dois segundos do `sleep(20)`): quem, quando chega, pra qual destino, por qual canal.</summary>
	private readonly List<(int Quem, long Em, DestinoDoRelogio Destino, string Canal)> _viagensDoRelogio = [];

	/// <summary>Os anuladores ligados: quem o carrega -> ate quando.</summary>
	private readonly Dictionary<int, long> _anuladorAte = [];

	/// <summary>Quem esta de capacete do "Henshin!". So memoria: e um enfeite que o relogio liga e desliga.</summary>
	private readonly HashSet<int> _henshin = [];

	private bool DeCapacete(ServerPlayer pl) => _henshin.Contains(pl.Id);

	/// <summary>SO PRA BANCADA: os corpos sem conexao contam como quem carrega um relogio. Falso em jogo, sempre.</summary>
	internal bool RelogioSemPeerDeTeste;

	/// <summary>Este corpo carrega um relogio deste canal?</summary>
	private static bool CarregaRelogioDoCanal(ServerPlayer p, string canal) =>
		p.Mochila.Pilhas.Exists(x => x.Quantidade > 0 && TechDeBolso.Base(x.Id) == Teleporte.Relogio
									 && (Teleporte.Canal(x.Id) == canal || Teleporte.RelogioIgnoraOCanalDeTeste));

	/// <summary>
	/// O relogio na mao de quem nao e o dono EXPLODE (`"[src] explodes!"`, `del(src)`) -- no canal, na tranca e no
	/// teleporte, que sao os tres que o original confere. Devolve se explodiu, e ai quem chamou para.
	/// </summary>
	private bool ORelogioExplodiu(ServerPlayer pl, ItemDef def)
	{
		if (!Teleporte.TrancadoPraOutro(Teleporte.Dna(def.Id), pl.Conta)) return false;
		pl.Mochila.Tirar(def.Id);
		MandarMochila(pl);
		MandarEfeito(pl, "explosao_final", 300);
		Falar(pl, Protocol.Fala.Emote, "mexe num relógio que não é dele: o relógio explode!");
		Avisar(pl, "o relógio explode na sua mão: ele estava travado pro DNA de outra pessoa.");
		return true;
	}

	/// <summary>`verb/Set()`: o canal do relogio.</summary>
	private void DefinirOCanalDoRelogio(ServerPlayer pl, ItemDef def, string cru)
	{
		if (ORelogioExplodiu(pl, def)) return;
		string canal = TechDeBolso.Limpo(cru, Teleporte.MaxDeCanal);
		if (canal.Length == 0) { Avisar(pl, "o canal não pode ser vazio."); return; }
		if (!TrocarItem(pl, def.Id, Teleporte.Escrever(canal, Teleporte.Dna(def.Id)))) { Avisar(pl, "sua mochila está cheia."); return; }
		Avisar(pl, $"canal do relógio: \"{canal}\".");
	}

	/// <summary>`verb/DNA_Authentication()`: o relogio passa a ser SO de quem o tranca. (La nao ha como destrancar; aqui o dono destranca.)</summary>
	private void TrancarORelogio(ServerPlayer pl, ItemDef def)
	{
		if (ORelogioExplodiu(pl, def)) return;
		bool trancado = Teleporte.Dna(def.Id).Length > 0;
		if (!TrocarItem(pl, def.Id, Teleporte.Escrever(Teleporte.Canal(def.Id), trancado ? "" : Teleporte.Marca(pl.Conta))))
		{ Avisar(pl, "sua mochila está cheia."); return; }
		Avisar(pl, trancado ? "trava de DNA desligada." : "trava de DNA ligada: na mão de outra pessoa, este relógio explode.");
	}

	/// <summary>
	/// OS DESTINOS DESTE CANAL, sempre na mesma ordem (os telepads pelo numero da obra, depois os relogios pelo nome
	/// de quem os carrega): `for(var/obj/items/I in teleportdest) ...` (`Teleports.dm:114-121`). Fica de fora o que
	/// esta onde nao se chega por teleporte (`ZonaFechadaAoTeleporte`), salvo pra quem ja esta la dentro.
	/// </summary>
	private List<DestinoDoRelogio> DestinosDoRelogio(ServerPlayer pl, string canal)
	{
		var destinos = new List<DestinoDoRelogio>();
		// SEM CANAL NAO HA DESTINO. (La o canal de fabrica e 0 nas duas pontas, e relogios e telepads recem-fabricados ja
		// se enxergam -- inclusive os de desconhecidos. Aqui o canal e escolhido antes.)
		if (canal.Length == 0 && !Teleporte.RelogioIgnoraOCanalDeTeste) return destinos;

		// `if(nI.telechannel == omnichannel && nI.Bolted && nI.x && nI.y && nI.z)`.
		foreach (Obra o in _noChao.Where(o => o.Tipo == Teleporte.Telepad && o.Aparafusada).OrderBy(o => o.Id))
		{
			string doTelepad = o.Tech?.Txt(Teleporte.ChaveCanal) ?? "";
			if (doTelepad != canal && !Teleporte.RelogioIgnoraOCanalDeTeste) continue;
			if (!o.Zona.Equals(pl.Zone) && ZonaFechadaAoTeleporte(o.Zona)) continue;
			destinos.Add(new DestinoDoRelogio($"telepad \"{doTelepad}\" em {NomeDoPlaneta(o.ZonaNome)}", o.Id, 0));
		}

		// `if(nI.omnichannel == omnichannel) teleportlist += nI`: os outros relogios, onde quer que quem os carrega esteja.
		foreach (ServerPlayer outro in _players.Values.Where(o => o != pl && (o.Peer != null || RelogioSemPeerDeTeste)).OrderBy(o => o.Name, StringComparer.Ordinal))
		{
			if (outro.Ficha.dead || !CarregaRelogioDoCanal(outro, canal)) continue;
			if (!outro.Zone.Equals(pl.Zone) && ZonaFechadaAoTeleporte(outro.Zone)) continue;
			destinos.Add(new DestinoDoRelogio($"o relógio de {NomeVisivel(outro)}", 0, outro.Id));
		}
		if (destinos.Count > Teleporte.MaxDeDestinos) destinos.RemoveRange(Teleporte.MaxDeDestinos, destinos.Count - Teleporte.MaxDeDestinos);
		return destinos;
	}

	/// <summary>
	/// ONDE O DESTINO ESTA AGORA. A chegada pergunta de novo, dois segundos depois do pedido, como la
	/// (`if(isturf(choice.loc)) ... else if(ismob(choice.loc))`, `Teleports.dm:128-139`): o relogio de alguem anda com
	/// ele. Falso = o destino sumiu (o telepad foi solto ou recolhido, quem carregava o relogio saiu ou o largou).
	/// </summary>
	private bool OndeEstaODestino(DestinoDoRelogio d, string canal, out ZoneKey zona, out Vec2 onde)
	{
		zona = default; onde = default;
		if (d.Telepad != 0)
		{
			Obra? o = _noChao.FirstOrDefault(x => x.Id == d.Telepad && x.Tipo == Teleporte.Telepad && x.Aparafusada);
			if (o == null) return false;
			zona = o.Zona;
			onde = PontoLivre(zona, new Vec2(o.X, o.Y)) ?? new Vec2(o.X, o.Y);
			return true;
		}
		if (!_players.TryGetValue(d.Portador, out ServerPlayer? p) || p.Ficha.dead || !CarregaRelogioDoCanal(p, canal)) return false;
		zona = p.Zone;
		onde = PontoAoLadoG11(p);   // (la, o mesmo tile; aqui o tile ao lado, como a Transmissao)
		return true;
	}

	private void ListarOsDestinos(ServerPlayer pl, ItemDef def)
	{
		if (ORelogioExplodiu(pl, def)) return;
		string canal = Teleporte.Canal(def.Id);
		if (canal.Length == 0) { Avisar(pl, "defina um canal no relógio antes."); return; }
		List<DestinoDoRelogio> destinos = DestinosDoRelogio(pl, canal);
		if (destinos.Count == 0) { Avisar(pl, $"nenhum telepad aparafusado nem outro relógio no canal \"{canal}\"."); return; }
		Avisar(pl, $"-- destinos do canal \"{canal}\" (viaje pelo número) --");
		for (int i = 0; i < destinos.Count; i++) Avisar(pl, $"  {i + 1}) {destinos[i].Nome}");
	}

	/// <summary>Ha um anulador ligado na zona deste corpo? (`if(Tn.z == usr.z &amp;&amp; Tn.telenulon)`.)</summary>
	private bool TeleporteAnuladoAqui(ServerPlayer pl)
	{
		if (Teleporte.AnuladorNaoAnulaDeTeste) return false;
		long agora = NowMs();
		foreach ((int quem, long ate) in _anuladorAte)
			if (ate > agora && _players.TryGetValue(quem, out ServerPlayer? dono) && dono.Zone.Hash == pl.Zone.Hash) return true;
		return false;
	}

	/// <summary>
	/// POR QUE O RELOGIO NAO LEVA ESTE CORPO. Vazio = leva. Sao as recusas do original
	/// (`if(!usr.canmove || usr.KO || usr.deathregening || usr.grabParalysis || usr.stagger) telecheck = 1` -- la,
	/// caladas) e a pergunta de todo teleporte deste port (`PorQueNaoSaiDaqui`: a prisao da Sala do Tempo, o selo, o
	/// interior de um Majin, a mente, a espera do torneio). O MORTO NAO VIAJA: quem morreu fica no Outro Mundo ate
	/// alguem o trazer de volta (dono, 2026-09-04).
	/// </summary>
	private string PorQueORelogioNaoLeva(ServerPlayer pl) =>
		pl.Ficha.dead ? "morto, o relógio não leva você a lugar nenhum."
		: pl.Ficha.KO ? "caído, você não aciona o relógio."
		: pl.Combate is { Stun: > 0 } ? "você ainda está atordoado."
		: Agarrado(pl) ? "preso num agarrão, você não alcança o relógio."
		: !PodeMexerOCorpo(pl) ? "você não consegue se mexer agora."
		: PorQueNaoSaiDaqui(pl);

	/// <summary>O "Teleport": confere, e marca a chegada pra dali a dois segundos.</summary>
	private void ViajarPeloRelogio(ServerPlayer pl, ItemDef def, string arg2)
	{
		if (ORelogioExplodiu(pl, def)) return;
		// `"Teleport failed!"`.
		if (TeleporteAnuladoAqui(pl)) { Avisar(pl, "o teleporte falhou: há um anulador ligado por aqui."); return; }
		if (PorQueORelogioNaoLeva(pl) is { Length: > 0 } porque) { Avisar(pl, porque); return; }
		if (_viagensDoRelogio.Exists(v => v.Quem == pl.Id)) { Avisar(pl, "o relógio já está levando você."); return; }

		string canal = Teleporte.Canal(def.Id);
		List<DestinoDoRelogio> destinos = DestinosDoRelogio(pl, canal);
		if (!int.TryParse(arg2, out int n) || n < 1 || n > destinos.Count)
		{ Avisar(pl, destinos.Count == 0 ? "não há destino nenhum neste canal." : $"escolha um destino de 1 a {destinos.Count} (veja a lista)."); return; }

		long espera = (long)(Teleporte.SegundosDeViagem * 1000);
		_viagensDoRelogio.Add((pl.Id, NowMs() + espera, destinos[n - 1], canal));
		// `usr.updateOverlay(/obj/overlay/effects/flickeffects/teleeffect)`: as faiscas dos dois segundos.
		MandarEfeito(pl, "transmissao", espera);
		Falar(pl, Protocol.Fala.Emote, "aciona o relógio e começa a se desfazer em faíscas...");
	}

	/// <summary>A chegada das viagens que venceram. Quem caiu, foi preso ou entrou num lugar anulado no meio nao chega.</summary>
	private void TickDosRelogios()
	{
		if (_viagensDoRelogio.Count == 0) return;
		long agora = NowMs();
		for (int i = _viagensDoRelogio.Count - 1; i >= 0; i--)
		{
			(int quem, long em, DestinoDoRelogio destino, string canal) = _viagensDoRelogio[i];
			if (agora < em) continue;
			_viagensDoRelogio.RemoveAt(i);
			if (!_players.TryGetValue(quem, out ServerPlayer? pl)) continue;

			// A CHEGADA E DOIS SEGUNDOS DEPOIS DO PEDIDO, e as perguntas se refazem: da pra cair, ser preso ou um anulador
			// ligar nesse meio -- e o destino pode ter sumido ou entrado onde nao se chega (`"[src]: Failed teleport!"`).
			if (pl.Ficha.KO || pl.Ficha.dead) continue;
			if (PorQueNaoSaiDaqui(pl) is { Length: > 0 } preso) { Avisar(pl, $"o teleporte falhou: {preso}"); continue; }
			if (TeleporteAnuladoAqui(pl)) { Avisar(pl, "o teleporte falhou: há um anulador ligado por aqui."); continue; }
			if (!OndeEstaODestino(destino, canal, out ZoneKey zona, out Vec2 onde) || !zona.Equals(pl.Zone) && ZonaFechadaAoTeleporte(zona))
			{ Avisar(pl, "o teleporte falhou: o destino não responde mais."); continue; }

			PuxarParaG4(pl, zona, onde);
			MandarEfeito(pl, "zanzoken", 500);   // `usr.emit_Sound('Instant_Pop.wav')`: o estalo de quem aparece
			Avisar(pl, $"o relógio leva você até {destino.Nome}.");
			if (destino.Portador != 0 && _players.TryGetValue(destino.Portador, out ServerPlayer? alvo))
				Avisar(alvo, $"{NomeVisivel(pl)} aparece ao seu lado, vindo pelo relógio!");
		}
	}

	/// <summary>
	/// O "Speak": a frase chega a todo relogio do canal (a quem o carrega -- quem fala inclusive) e aos comunicadores
	/// que escutam o canal (`Teleports.dm:141-154`). La ele nao confere o dono do relogio, e aqui tambem nao.
	/// </summary>
	private void FalarPeloRelogio(ServerPlayer pl, ItemDef def, string cru)
	{
		if (pl.Ficha.KO) return;   // `if(!usr.KO)`
		string canal = Teleporte.Canal(def.Id);
		if (canal.Length == 0) { Avisar(pl, "defina um canal no relógio antes."); return; }
		string frase = TextoLivre(cru, Vigilancia.MaxDeFrase);
		if (frase.Length == 0) return;
		if (EstaCalado(pl)) { Avisar(pl, "você está silenciado."); return; }

		// `"(Watch)<[usr.SayColor]>[usr] says, '[msg]'"`.
		string linha = $"(Relógio) {NomeVisivel(pl)} diz: \"{frase}\"";
		foreach (ServerPlayer o in _players.Values.Where(o => o.Peer != null || RelogioSemPeerDeTeste))
			if (o == pl || CarregaRelogioDoCanal(o, canal)) Avisar(o, linha);
		EntregarAosComunicadores([canal], linha);
	}

	/// <summary>
	/// O "Henshin!": o grito (`"[usr.name] shouts, 'Transform!!'"`), e o capacete que veste e tira. O brilho de vento
	/// dos dois segundos (`henshineffect`) nao veio.
	/// </summary>
	private void Henshin(ServerPlayer pl)
	{
		Falar(pl, Protocol.Fala.Diz, "Transform!!");
		if (!_henshin.Add(pl.Id)) _henshin.Remove(pl.Id);
		ReapresentarOCorpo(pl);
	}

	// =====================================================================
	// O ANULADOR DE TELEPORTE -- `Teleports.dm:29-63`
	// =====================================================================
	/// <summary>O `Click()`: liga (pelo tempo do `Teleporte.SegundosDoAnulador`) e desliga.</summary>
	private void LigarOAnulador(ServerPlayer pl)
	{
		if (_anuladorAte.TryGetValue(pl.Id, out long ate) && ate > NowMs())
		{
			_anuladorAte.Remove(pl.Id);
			// `to_chat(view(src), "[src] turned off!!!")`.
			Falar(pl, Protocol.Fala.Emote, "desliga um anulador de teleporte.");
			Avisar(pl, "anulador desligado.");
			return;
		}
		double segundos = Teleporte.SegundosDoAnulador(pl.Ficha.techskill);
		_anuladorAte[pl.Id] = NowMs() + (long)(segundos * 1000);
		// `to_chat(view(src), "[src] turned on!!!")`.
		Falar(pl, Protocol.Fala.Emote, "liga um anulador de teleporte!");
		Avisar(pl, $"anulador ligado por {segundos:0} segundos: nenhum relógio teleporta ninguém neste lugar -- nem você.");
	}

	/// <summary>Os anuladores que venceram se desligam, e quem os carrega fica sabendo (`"[src] turned off!!!"`).</summary>
	private void TickDosAnuladores()
	{
		if (_anuladorAte.Count == 0) return;
		long agora = NowMs();
		foreach (int id in _anuladorAte.Where(a => agora >= a.Value).Select(a => a.Key).ToList())
		{
			_anuladorAte.Remove(id);
			if (_players.TryGetValue(id, out ServerPlayer? dono)) Avisar(dono, "o anulador de teleporte se desligou.");
		}
	}

	// =====================================================================
	// AS CAMERAS -- `Camera.dm`. A regra esta em `Vigilancia`.
	// =====================================================================
	/// <summary>`verb/Frequencies()` da camera: `frequency = input(usr,"Type a frequency") as text` -- de quem estiver do lado, como la.</summary>
	private void DefinirAFrequenciaDaCamera(ServerPlayer pl, string cru)
	{
		Obra? o = ObraQueAceita(pl, Vigilancia.VerboFrequencia);
		if (o == null) { Avisar(pl, "não há câmera por perto."); return; }
		string frequencia = Vigilancia.FrequenciaLimpa(cru);
		if (frequencia.Length == 0) { Avisar(pl, "a frequência não pode ser vazia."); return; }
		(o.Tech ??= new EstadoDeTech()).S[Vigilancia.ChaveFrequencia] = frequencia;
		GravarMundo();
		Avisar(pl, $"frequência da câmera: \"{frequencia}\"" + (o.Aparafusada ? "." : " -- ela só transmite aparafusada."));
	}

	/// <summary>
	/// O QUE AS CAMERAS DESTAS FREQUENCIAS VEEM AGORA -- o `View_Cameras` do original, por escrito (ver o cabecalho do
	/// `Vigilancia`): pra cada camera, quem esta a vista dela e o que faz. Nenhum numero de poder, e ninguem que
	/// esteja invisivel.
	/// </summary>
	private void RelatarAsCameras(ServerPlayer pl, ICollection<string> frequencias)
	{
		// SEM FREQUENCIA DEFINIDA A CAMERA NAO TRANSMITE. (La a de fabrica e `00000` nas duas pontas, e todo computador
		// novo ve toda camera nova.)
		var cameras = _noChao.Where(o => o.Tipo == Vigilancia.Camera && o.Aparafusada
										 && (Vigilancia.CameraIgnoraAFrequenciaDeTeste
											 || o.Tech?.Txt(Vigilancia.ChaveFrequencia) is { Length: > 0 } f && frequencias.Contains(f)))
								 .OrderBy(o => o.Id).ToList();
		if (cameras.Count == 0) { Avisar(pl, "nenhuma câmera responde nessa frequência."); return; }

		float raio = Vigilancia.RaioDaCameraEmTiles * ZoneCollision.TileSize;
		foreach (Obra cam in cameras)
		{
			var vistos = new List<string>();
			if (_zones.TryGetValue(cam.Zona.Hash, out List<ServerPlayer>? naZona))
				foreach (ServerPlayer o in naZona)
				{
					if (EstaOculto(o.Id) || Math.Abs(o.Pos.X - cam.X) > raio || Math.Abs(o.Pos.Y - cam.Y) > raio) continue;
					vistos.Add($"{NomeVisivel(o)} ({OQueFaz(o)})");
				}
			Avisar(pl, $"câmera \"{cam.Tech?.Txt(Vigilancia.ChaveFrequencia)}\" em {NomeDoPlaneta(cam.ZonaNome)}: "
					   + (vistos.Count == 0 ? "ninguém à vista." : string.Join(", ", vistos) + "."));
		}
	}

	/// <summary>O que um corpo esta fazendo, numa palavra -- pra o relato da camera.</summary>
	private static string OQueFaz(ServerPlayer o) =>
		o.Ficha.dead ? "morto"
		: o.Ficha.KO ? "caído"
		: o.Combate is { EmCombate: > 0 } ? "lutando"
		: o.Ficha.train ? "treinando"
		: o.Ficha.med ? "meditando"
		: o.Voando ? "voando"
		: o.Moving ? "andando" : "parado";

	/// <summary>`verb/Frequency()` do computador de cameras: uma frequencia, que mora no id dele.</summary>
	private void DefinirAFrequenciaDoComputador(ServerPlayer pl, ItemDef def, string cru)
	{
		string frequencia = Vigilancia.FrequenciaLimpa(cru);
		if (frequencia.Length == 0) { Avisar(pl, "a frequência não pode ser vazia."); return; }
		if (!TrocarItem(pl, def.Id, Vigilancia.EscreverComputador(frequencia, Vigilancia.DnaDoComputador(def.Id))))
		{ Avisar(pl, "sua mochila está cheia."); return; }
		Avisar(pl, $"frequência: \"{frequencia}\".");
	}

	/// <summary>`verb/Frequencies()` do PDA: "Add" e "Subtract" numa lista que mora no id dele.</summary>
	private void MexerNasFrequenciasDoPda(ServerPlayer pl, ItemDef def, string cru, bool tirar)
	{
		string frequencia = Vigilancia.FrequenciaLimpa(cru);
		if (frequencia.Length == 0) { Avisar(pl, "a frequência não pode ser vazia."); return; }
		List<string> lista = Vigilancia.FrequenciasDoPda(def.Id);
		if (tirar)
		{
			if (!lista.Remove(frequencia)) { Avisar(pl, $"o PDA não está na frequência \"{frequencia}\"."); return; }
		}
		else
		{
			if (lista.Contains(frequencia)) { Avisar(pl, $"o PDA já está na frequência \"{frequencia}\"."); return; }
			if (lista.Count >= Vigilancia.MaxDeFrequenciasNoPda) { Avisar(pl, $"o PDA guarda no máximo {Vigilancia.MaxDeFrequenciasNoPda} frequências."); return; }
			lista.Add(frequencia);
		}
		if (!TrocarItem(pl, def.Id, Vigilancia.EscreverPda(TechDeBolso.Campo(def.Id, 0), lista, Vigilancia.DnaDoPda(def.Id))))
		{ Avisar(pl, "sua mochila está cheia."); return; }
		Avisar(pl, lista.Count == 0 ? "o PDA não está mais em frequência nenhuma." : $"frequências do PDA: {string.Join(", ", lista)}.");
	}

	/// <summary>`verb/View_Cameras()` do computador e do PDA.</summary>
	private void VerAsCamerasDoItem(ServerPlayer pl, ItemDef def)
	{
		// `"Wrong signature!"` -- a tranca que no original nunca recusa (ver o cabecalho do `Vigilancia`).
		if (Teleporte.TrancadoPraOutro(Vigilancia.DnaDoItem(def.Id), pl.Conta)) { Avisar(pl, "assinatura errada!"); return; }
		List<string> frequencias = TechDeBolso.Base(def.Id) == TechDeBolso.Pda
			? Vigilancia.FrequenciasDoPda(def.Id)
			: Vigilancia.Frequencias(Vigilancia.FrequenciaDoComputador(def.Id));
		if (frequencias.Count == 0) { Avisar(pl, "defina a frequência antes."); return; }
		RelatarAsCameras(pl, frequencias);
	}

	/// <summary>`verb/Toggle_DNA_Lock()` do computador de cameras e do PDA. (La qualquer um destranca; aqui, so quem trancou.)</summary>
	private void TrancarOItemDeCameras(ServerPlayer pl, ItemDef def)
	{
		string dna = Vigilancia.DnaDoItem(def.Id);
		if (Teleporte.TrancadoPraOutro(dna, pl.Conta)) { Avisar(pl, "assinatura errada!"); return; }
		string marca = dna.Length > 0 ? "" : Teleporte.Marca(pl.Conta);
		string novo = TechDeBolso.Base(def.Id) == TechDeBolso.Pda
			? Vigilancia.EscreverPda(TechDeBolso.Campo(def.Id, 0), Vigilancia.FrequenciasDoPda(def.Id), marca)
			: Vigilancia.EscreverComputador(Vigilancia.FrequenciaDoComputador(def.Id), marca);
		if (!TrocarItem(pl, def.Id, novo)) { Avisar(pl, "sua mochila está cheia."); return; }
		Avisar(pl, dna.Length > 0 ? "DNA destrancado." : "DNA trancado: só você vê as câmeras por ele.");   // `"DNA unlocked."` / `"DNA locked"`
	}

	// =====================================================================
	// A BIBLIOTECA DO PDA -- `Upload_to_Library` e `Print_from_Library` (`Camera.dm:87-114`)
	// =====================================================================
	/// <summary>
	/// Publica um livro ESCRITO da mochila na biblioteca global: o texto e copiado, o livro fica. La a escolha e uma
	/// lista (`input ... in uploads`); aqui e o numero do livro, o que aparece no nome dele.
	/// </summary>
	private void PublicarNaBiblioteca(ServerPlayer pl, string arg2)
	{
		var escritos = new List<(int Numero, string Texto)>();
		foreach (Pilha p in pl.Mochila.Pilhas)
			if (p.Quantidade > 0 && TechDeBolso.Base(p.Id) == TechDeBolso.Livro
				&& _cofreDeTech.Textos.TryGetValue(NumeroDoCofre(p.Id), out string? escrito) && escrito.Length > 0)
				escritos.Add((NumeroDoCofre(p.Id), escrito));
		// `"You have no books to upload!"`.
		if (escritos.Count == 0) { Avisar(pl, "você não tem nenhum livro escrito pra publicar."); return; }
		if (!int.TryParse(arg2, out int n) || !escritos.Exists(e => e.Numero == n))
		{ Avisar(pl, $"qual livro? Os seus: {string.Join(", ", escritos.Select(e => "nº " + e.Numero))}."); return; }

		string texto = escritos.Find(e => e.Numero == n).Texto;
		if (_cofreDeTech.Biblioteca.Contains(texto)) { Avisar(pl, "esse livro já está na biblioteca."); return; }
		if (_cofreDeTech.Biblioteca.Count >= Vigilancia.MaxDeLivrosNaBiblioteca) { Avisar(pl, "a biblioteca está cheia."); return; }
		_cofreDeTech.Biblioteca.Add(texto);
		GravarOCofreDeTech();
		Avisar(pl, $"livro publicado na biblioteca global (nº {_cofreDeTech.Biblioteca.Count}), pras gerações futuras.");
	}

	private void ListarABiblioteca(ServerPlayer pl)
	{
		if (_cofreDeTech.Biblioteca.Count == 0) { Avisar(pl, "a biblioteca global está vazia."); return; }
		Avisar(pl, $"-- biblioteca global: {_cofreDeTech.Biblioteca.Count} livro(s) (imprima pelo número) --");
		for (int i = 0; i < _cofreDeTech.Biblioteca.Count; i++)
		{
			string t = _cofreDeTech.Biblioteca[i];
			Avisar(pl, $"  {i + 1}) {(t.Length > 48 ? t[..48] + "..." : t)}");
		}
	}

	/// <summary>Imprime um livro da biblioteca: um livro novo, com o texto de la, cai na mochila (la, no chao).</summary>
	private void ImprimirDaBiblioteca(ServerPlayer pl, string arg2)
	{
		if (!int.TryParse(arg2, out int n) || n < 1 || n > _cofreDeTech.Biblioteca.Count)
		{ Avisar(pl, _cofreDeTech.Biblioteca.Count == 0 ? "a biblioteca global está vazia." : $"escolha um livro de 1 a {_cofreDeTech.Biblioteca.Count}."); return; }
		if (pl.Mochila.Cheio) { Avisar(pl, "sua mochila está cheia."); return; }
		int numero = _cofreDeTech.Proximo++;
		_cofreDeTech.Textos[numero] = _cofreDeTech.Biblioteca[n - 1];
		GravarOCofreDeTech();
		Guardar(pl, TechDeBolso.Com(TechDeBolso.Livro, numero.ToString()));
		Avisar(pl, $"o PDA imprime o livro nº {n} da biblioteca.");
	}

	// =====================================================================
	// O COMUNICADOR -- `Communicator.dm`
	// =====================================================================
	private Obra? ComunicadorPerto(ServerPlayer pl, string verbo)
	{
		Obra? o = ObraQueAceita(pl, verbo);
		if (o == null) Avisar(pl, "não há comunicador por perto.");
		return o;
	}

	/// <summary>A tranca de DNA do comunicador recusa este corpo? (`siglock`: guarda as cameras e o historico.)</summary>
	private bool ComunicadorTrancadoPara(Obra o, ServerPlayer pl)
	{
		if (!Teleporte.TrancadoPraOutro(o.Tech?.Txt(Vigilancia.ChaveDna) ?? "", pl.Conta)) return false;
		Avisar(pl, "assinatura errada!");
		return true;
	}

	/// <summary>`verb/Frequencies()`: "Add" e "Subtract". Como la, a tranca nao guarda as frequencias.</summary>
	private void MexerNasFrequencias(ServerPlayer pl, string cru, bool tirar)
	{
		Obra? o = ComunicadorPerto(pl, tirar ? Vigilancia.VerboMenosFrequencia : Vigilancia.VerboMaisFrequencia);
		if (o == null) return;
		string frequencia = Vigilancia.FrequenciaLimpa(cru);
		if (frequencia.Length == 0) { Avisar(pl, "a frequência não pode ser vazia."); return; }
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		List<string> lista = Vigilancia.Frequencias(e.Txt(Vigilancia.ChaveFrequencias));
		if (tirar)
		{
			if (!lista.Remove(frequencia)) { Avisar(pl, $"o comunicador não escuta \"{frequencia}\"."); return; }
		}
		else
		{
			if (lista.Contains(frequencia)) { Avisar(pl, $"o comunicador já escuta \"{frequencia}\"."); return; }
			if (lista.Count >= Vigilancia.MaxDeFrequencias) { Avisar(pl, $"o comunicador escuta no máximo {Vigilancia.MaxDeFrequencias} frequências."); return; }
			lista.Add(frequencia);
		}
		e.S[Vigilancia.ChaveFrequencias] = string.Join(",", lista);
		GravarMundo();
		Avisar(pl, lista.Count == 0 ? "o comunicador não escuta mais nenhuma frequência." : $"o comunicador escuta: {string.Join(", ", lista)}.");
	}

	/// <summary>`verb/Frequencies()`: "Check".</summary>
	private void DizerAsFrequencias(ServerPlayer pl)
	{
		Obra? o = ComunicadorPerto(pl, Vigilancia.VerboFrequencias);
		if (o == null) return;
		List<string> lista = Vigilancia.Frequencias(o.Tech?.Txt(Vigilancia.ChaveFrequencias) ?? "");
		Avisar(pl, lista.Count == 0 ? "o comunicador ainda não escuta nenhuma frequência." : $"o comunicador escuta: {string.Join(", ", lista)}.");
	}

	/// <summary>
	/// A FRASE CHEGA AOS COMUNICADORES que escutam uma destas frequencias -- o de quem manda inclusive, que escuta as
	/// dele: vai pro historico de cada um, e o que tem difusor a diz em voz alta a quem esta perto (ver o cabecalho do
	/// `Vigilancia`). Devolve quantos a receberam.
	/// </summary>
	private int EntregarAosComunicadores(ICollection<string> frequencias, string linha)
	{
		int entregues = 0;
		foreach (Obra o in _noChao)
		{
			if (o.Tipo != Vigilancia.Comunicador || o.Tech is not { } e) continue;
			if (!Vigilancia.Frequencias(e.Txt(Vigilancia.ChaveFrequencias)).Exists(frequencias.Contains)) continue;
			List<string> historico = [.. e.Txt(Vigilancia.ChaveHistorico).Split('\n', StringSplitOptions.RemoveEmptyEntries)];
			historico.Add(linha);
			if (historico.Count > Vigilancia.MaxDeHistorico) historico.RemoveRange(0, historico.Count - Vigilancia.MaxDeHistorico);
			e.S[Vigilancia.ChaveHistorico] = string.Join('\n', historico);
			if (e.Ligado(Vigilancia.ChaveDifusor)) AnunciarAVista(o, linha);   // `if(hasbroadcaster) to_chat(view(nO), ...)`
			entregues++;
		}
		if (entregues > 0) GravarMundo();
		return entregues;
	}

	/// <summary>`verb/Send(msg)`: a frase vai pra todo aparelho que escuta uma das frequencias deste comunicador.</summary>
	private void EnviarPeloComunicador(ServerPlayer pl, string cru)
	{
		Obra? o = ComunicadorPerto(pl, Vigilancia.VerboEnviar);
		if (o == null) return;
		string frase = TextoLivre(cru, Vigilancia.MaxDeFrase);
		if (frase.Length == 0) return;
		if (EstaCalado(pl)) { Avisar(pl, "você está silenciado."); return; }
		List<string> lista = Vigilancia.Frequencias(o.Tech?.Txt(Vigilancia.ChaveFrequencias) ?? "");
		if (lista.Count == 0) { Avisar(pl, "o comunicador não está em frequência nenhuma: adicione uma."); return; }

		// `"(Communications)<[usr.SayColor]>[usr] says, '[msg]'"`.
		string linha = $"(Comunicações) {NomeVisivel(pl)} diz: \"{frase}\"";
		int ouviram = 0;
		foreach (ServerPlayer ouvinte in _players.Values.Where(x => x != pl && (x.Peer != null || RelogioSemPeerDeTeste)))
		{
			// OS RELOGIOS do canal (`nO.omnichannel in freqlist`) e OS SCOUTERS ligados na frequencia (`nO.channel in freqlist`).
			bool relogio = ouvinte.Mochila.Pilhas.Exists(p => p.Quantidade > 0 && TechDeBolso.Base(p.Id) == Teleporte.Relogio && lista.Contains(Teleporte.Canal(p.Id)));
			bool scouter = TemScouter(ouvinte) && lista.Contains(FrequenciaDe(ouvinte).ToString());
			if (!relogio && !scouter) continue;
			Avisar(ouvinte, linha);
			ouviram++;
			if (Vigilancia.EnvioParaNoPrimeiroDeTeste) break;
		}
		// (o proprio console esta entre os que escutam as frequencias dele: a linha fica no historico de quem manda tambem)
		if (!(Vigilancia.EnvioParaNoPrimeiroDeTeste && ouviram > 0)) ouviram += Math.Max(EntregarAosComunicadores(lista, linha) - 1, 0);
		Avisar(pl, ouviram == 0 ? "a frase sai -- e ninguém está escutando essas frequências agora." : $"enviado ({ouviram} aparelho(s) escutando).");
	}

	/// <summary>`verb/Megaphone(msg)`: pra todo mundo, com a espera de um minuto que cresce a cada uso.</summary>
	private void GritarNoMegafone(ServerPlayer pl, string cru)
	{
		Obra? o = ComunicadorPerto(pl, Vigilancia.VerboMegafone);
		if (o == null) return;
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		// `"You need the upgrade."`.
		if (!e.Ligado(Vigilancia.ChaveMegafone)) { Avisar(pl, "o comunicador precisa da melhoria do megafone."); return; }
		string frase = TextoLivre(cru, Vigilancia.MaxDeFrase);
		if (frase.Length == 0) return;
		if (EstaCalado(pl)) { Avisar(pl, "você está silenciado."); return; }

		long agora = NowMs();
		if (agora < e.Num(Vigilancia.ChaveMegaAte))
		{ Avisar(pl, $"o megafone ainda esfria: {(e.Num(Vigilancia.ChaveMegaAte) - agora) / 1000:0} s."); return; }
		// A PILHA CAI UM POR MINUTO PARADO, contado de quando o megafone ficou pronto (`cooldowndown = 600`).
		double pilha = e.Num(Vigilancia.ChaveMegaPilha);
		double decaiEm = e.Num(Vigilancia.ChaveMegaDecaiEm);
		while (pilha > 0 && decaiEm > 0 && agora >= decaiEm) { pilha--; decaiEm += Vigilancia.SegundosDoMegafonePorUso * 1000; }

		AnunciarNoMundo($"(Megafone) {NomeVisivel(pl)} diz: \"{frase}\"");
		pilha++;                                                                   // `cooldownstack += 1`
		double espera = Vigilancia.SegundosDoMegafonePorUso * pilha * 1000;        // `megaphonecooldown = 600 * cooldownstack`
		e.N[Vigilancia.ChaveMegaPilha] = pilha;
		e.N[Vigilancia.ChaveMegaAte] = agora + espera;
		e.N[Vigilancia.ChaveMegaDecaiEm] = agora + espera + Vigilancia.SegundosDoMegafonePorUso * 1000;
		GravarMundo();
		Avisar(pl, $"o megafone leva a sua frase ao universo inteiro. Ele esfria por {espera / 1000:0} s.");
	}

	/// <summary>`verb/Upgrades()`: megafone, difusor e camera -- cada um com a tecnologia dele, em zeni ou (o dobro) em recursos.</summary>
	private void MelhorarOComunicador(ServerPlayer pl, string arg)
	{
		Obra? o = ComunicadorPerto(pl, Vigilancia.VerboMelhorar);
		if (o == null) return;
		(string oQue, Moeda moeda) = Recurso.LerPedidoPago(arg);
		(string chave, double custo, double tecnologia, string nome) = oQue switch
		{
			Vigilancia.Megafone => (Vigilancia.ChaveMegafone, Vigilancia.CustoDoMegafone, Vigilancia.TecnologiaDoMegafone, "o megafone"),
			Vigilancia.Difusor => (Vigilancia.ChaveDifusor, Vigilancia.CustoDoDifusor, Vigilancia.TecnologiaDoDifusor, "o difusor"),
			Vigilancia.OlhoDeCamera => (Vigilancia.ChaveOlho, Vigilancia.CustoDoOlho, Vigilancia.TecnologiaDoOlho, "a câmera"),
			_ => ("", 0, 0, ""),
		};
		if (chave.Length == 0) { Avisar(pl, "o comunicador não tem essa melhoria."); return; }
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		if (e.Ligado(chave)) { Avisar(pl, $"o comunicador já tem {nome}."); return; }
		// `if(usr.techskill>=35 && ...)`.
		if (pl.Ficha.techskill < tecnologia) { Avisar(pl, $"{nome} pede {tecnologia:0} de tecnologia."); return; }
		if (!CobrarOuExplicar(pl, custo, moeda, nome)) return;
		e.N[chave] = 1;
		GravarMundo();
		Avisar(pl, $"comunicador melhorado: {nome}.");
	}

	/// <summary>`verb/Camera()`: com a melhoria, o comunicador ve as cameras de TODAS as frequencias dele.</summary>
	private void VerAsCamerasDoComunicador(ServerPlayer pl)
	{
		Obra? o = ComunicadorPerto(pl, Vigilancia.VerboCameras);
		if (o == null) return;
		// `"No upgrade!"`.
		if (o.Tech?.Ligado(Vigilancia.ChaveOlho) != true) { Avisar(pl, "o comunicador precisa da melhoria da câmera."); return; }
		if (ComunicadorTrancadoPara(o, pl)) return;
		List<string> lista = Vigilancia.Frequencias(o.Tech.Txt(Vigilancia.ChaveFrequencias));
		if (lista.Count == 0) { Avisar(pl, "o comunicador não está em frequência nenhuma."); return; }
		RelatarAsCameras(pl, lista);
	}

	/// <summary>`verb/Communication_History()`.</summary>
	private void VerOHistorico(ServerPlayer pl)
	{
		Obra? o = ComunicadorPerto(pl, Vigilancia.VerboHistorico);
		if (o == null || ComunicadorTrancadoPara(o, pl)) return;
		string historico = o.Tech?.Txt(Vigilancia.ChaveHistorico) ?? "";
		if (historico.Length == 0) { Avisar(pl, "o histórico está vazio."); return; }
		Avisar(pl, "-- histórico do comunicador --");
		foreach (string linha in historico.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Avisar(pl, linha);
	}

	/// <summary>`verb/DNA_Lock()`: qualquer um tranca o que esta destrancado, e so quem trancou destranca.</summary>
	private void TrancarOComunicador(ServerPlayer pl)
	{
		Obra? o = ComunicadorPerto(pl, Vigilancia.VerboDna);
		if (o == null || ComunicadorTrancadoPara(o, pl)) return;
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		bool trancado = e.Txt(Vigilancia.ChaveDna).Length > 0;
		e.S[Vigilancia.ChaveDna] = trancado ? "" : Teleporte.Marca(pl.Conta);
		GravarMundo();
		Avisar(pl, trancado ? "DNA destrancado." : "DNA trancado: só você vê as câmeras e lê o histórico por ele.");
	}

	// =====================================================================
	// O RELOGIO DO LOTE, E A SAIDA
	// =====================================================================
	private void TickDasComunicacoes()
	{
		TickDosRelogios();
		TickDosAnuladores();
	}

	/// <summary>Quem saiu do mundo leva o anulador, a viagem e o capacete dele.</summary>
	private void EsquecerComunicacoes(int id)
	{
		_anuladorAte.Remove(id);
		_henshin.Remove(id);
		_viagensDoRelogio.RemoveAll(v => v.Quem == id);
	}
}
