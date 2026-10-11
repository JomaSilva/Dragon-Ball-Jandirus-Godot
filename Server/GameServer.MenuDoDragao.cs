using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// **O MENU DO DRAGAO** -- a janela de desejos, do lado do servidor: a lista que cada jogador ve, o motivo de
/// cada linha apagada, quem cada desejo alcanca, e quando a janela abre, se acerta e fecha.
///
/// ============================ O PEDIDO DO DONO (2026-10-10) ============================
/// *"ao invocar o dragao das esferas, deveria aparecer um menu com varios desejos q vc pode escolher e caso
/// tenha um target o jogo vai perguntar se e pra vc ou pra outra pessoa q vc pode escolher e nao ter q abrir um
/// menu e colocar um id de pedido."*
///
/// Ate aqui o dragao subia, despejava no chat uma linha por desejo com o id entre colchetes, e a aba World tinha
/// uma caixa de texto pra devolver o id e o NOME do alvo. O DM nunca fez assim: ele abre `input() in WishList`
/// e depois `input() in deadlist` (`WishTable.dm:150` e `:225`) -- duas caixas, a lista e o alvo. As duas
/// caixas viraram as duas etapas de uma janela, e as duas listas viajam prontas neste pacote.
/// ======================================================================================
///
/// ============================ NADA DAQUI CONCEDE DESEJO ============================
/// Este arquivo MONTA e MANDA. Cada linha do menu carrega o verbo de producao que a pede (`db_desejar`,
/// `sdb_invocar`, `sdb_pedido`...) e o argumento ja escrito; a janela devolve os dois como vieram, e quem
/// atende e o mesmo `PedirDesejo`/`ChamarOSuperShenron` de sempre, conferindo tudo de novo. O cliente so
/// escolhe -- e um cliente mexido que mande outra coisa esbarra nos portoes que ja existiam.
///
/// E O QUE O MENU APAGA SAI DAS MESMAS PERGUNTAS QUE O VERBO FAZ (`OCriadorNaoPede`, `JaPediuMarcos`,
/// `MortosQueVoltam`, `VivosAoAlcanceDoDragao`...): uma funcao pras duas pontas, pra a janela nao oferecer o que
/// o dragao recusaria nem esconder o que ele atenderia. A bancada `--menudodragaoteste` cobra as duas metades.
/// ==================================================================================
///
/// ============================ QUEM RECEBE, E QUANDO ============================
///   * ABRE SOZINHA pra quem invocou, no instante em que o dragao sobe (`AbrirOsDesejos`); pra quem recebe uma
///     oferta de guarda das Super Esferas; pra quem tem de dizer o preco do desejo supremo; pra quem emprestou
///     as sete, na hora de escolher o pedido. Sao os `alert()`/`input()` do DM, um a um.
///   * ABRE A PEDIDO pelo botao "Ver os desejos" da aba World (`db_menu`).
///   * SE ACERTA sozinha enquanto esta aberta: o servidor guarda quem esta olhando e a assinatura do que mandou
///     por ultimo, e manda outro menu so quando ela muda (a contagem caiu, alguem chegou ao alcance, o alvo
///     marcado trocou). Estado sincronizado, e nao pergunta repetida.
///   * FECHA quando o dragao que ela mostrava vai embora.
/// ==============================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// QUEM ESTA OLHANDO
	// =====================================================================
	/// <summary>
	/// UMA JANELA DE DESEJOS ABERTA: o que o servidor mandou por ultimo a este jogador, e o que ele olhava.
	/// </summary>
	private sealed class OlharNoDragao
	{
		/// <summary>O que ele pediu pra ver: o que couber aqui (<see cref="QualDragao.Nenhum"/>) ou o Super.</summary>
		public QualDragao Qual;

		/// <summary>A <see cref="MenuDoDragao.Assinatura"/> do ultimo menu mandado.</summary>
		public string Assinatura = "";

		/// <summary>De que dragao era esse menu.</summary>
		public QualDragao Dragao;

		/// <summary>O set do menu (o dragao comum), ou -1.</summary>
		public int Set = -1;

		/// <summary>Havia um dragao de pe nesse set quando o menu saiu -- dele ou de outro.</summary>
		public bool DragaoDePe;

		/// <summary>O ciclo das Super Esferas quando o menu saiu: andou, as sete se foram.</summary>
		public int Ciclo;
	}

	/// <summary>
	/// As janelas abertas, por id de SESSAO. Transiente como o <see cref="_invocacoes"/> e pelo mesmo motivo:
	/// nao vai pro disco, e cai na limpeza total (<see cref="ZerarEsferas"/>).
	/// </summary>
	private readonly Dictionary<int, OlharNoDragao> _olhandoODragao = [];

	/// <summary>
	/// O `S2C.MenuDoDragao` QUE SAIU, por destinatario: o menu montado e os bytes do fio. Nulo fora da bancada,
	/// o molde da <see cref="EscutaDoFioDasEsferas"/>. Anota tambem o que iria pra um corpo sem dono na tela:
	/// a bancada mede o menu de corpos forjados, e pra eles o `Send` e que nao acontece.
	/// </summary>
	internal static List<(int Para, MenuDoDragao Menu, byte[] Fio)>? EscutaDosMenusDoDragao;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--menudodragaoteste`): o dragao vai embora e ninguem avisa a janela de quem o
	/// olhava -- ela fica na tela oferecendo pedidos a um dragao que nao existe. Falso em jogo, sempre.
	/// </summary>
	internal bool DragaoSomeCaladoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--menudodragaoteste`): o menu sai de novo a cada sincronia, mude ou nao -- um
	/// pacote por segundo pra cada janela aberta, pra sempre. Falso em jogo, sempre.
	/// </summary>
	internal bool MenuSaiTodoTiqueDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--menudodragaoteste`): o menu nao apaga linha nenhuma -- oferece ao criador o
	/// desejo de poder que encerraria a invocacao dele, e os Marcos a quem ja os pediu. Falso em jogo, sempre.
	/// </summary>
	internal bool MenuNaoApagaNadaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--menudodragaoteste`): "Pedidos por invocação" aparece pra quem ergueu a estatua
	/// do dragao que esta olhando, esteja ele no mundo que estiver -- e o gesto reconfiguraria a estatua do mundo
	/// ERRADO (o verbo age na do mundo em que se esta). Falso em jogo, sempre.
	/// </summary>
	internal bool GestoDeOutroMundoDeTeste;

	// =====================================================================
	// O VERBO DA JANELA
	// =====================================================================
	/// <summary>
	/// **`db_menu [super | comum | foco &lt;linha&gt; | fim]`** -- o botao "Ver os desejos" da aba World, e a
	/// janela avisando que fechou.
	///
	///   * sem argumento: o menu que couber aqui (ver <see cref="RetratoDoDragaoDe"/>);
	///   * `super`: o do Super Shenron, esteja o jogador onde estiver (o gesto "Super Shenron" do menu comum);
	///   * `foco derrubar`: abre ja na linha -- o botao "Derrubar a estatua" da aba;
	///   * `fim`: a janela fechou; o servidor para de acerta-la.
	/// </summary>
	private void PedirOMenuDoDragao(ServerPlayer pl, string arg)
	{
		string a = arg.Trim();
		if (string.Equals(a, "fim", StringComparison.OrdinalIgnoreCase)) { _olhandoODragao.Remove(pl.Id); return; }

		QualDragao qual = QualDragao.Nenhum;
		string foco = "";
		if (string.Equals(a, "super", StringComparison.OrdinalIgnoreCase)) qual = QualDragao.Super;
		else if (string.Equals(a, "comum", StringComparison.OrdinalIgnoreCase)) qual = QualDragao.Comum;
		else if (a.StartsWith("foco ", StringComparison.OrdinalIgnoreCase)) foco = a[5..].Trim();

		MenuDoDragao m = MandarOMenuDoDragao(pl, abrir: true, foco, qual);

		// A LINHA PEDIDA NAO ESTA NESTE MENU: quem apertou "Derrubar a estatua" longe dela (ou diante da
		// ancestral de Namek) le o motivo nas palavras do proprio verbo, em vez de uma janela que abriu sem
		// dizer por que a linha nao veio.
		if (foco == AcoesDoDragao.Derrubar && m.Entrada(foco) == null) DerrubarAEstatua(pl, "");
	}

	// =====================================================================
	// MANDAR, ACERTAR E FECHAR
	// =====================================================================
	/// <summary>
	/// MONTA O MENU DESTE JOGADOR, MANDA, e passa a acerta-lo enquanto a janela estiver aberta.
	/// </summary>
	/// <param name="abrir">O servidor PEDE a janela agora. Falso = so atualiza quem ja esta olhando.</param>
	/// <param name="foco">A linha em que a janela ja abre (um dos <see cref="AcoesDoDragao"/>), ou vazio.</param>
	/// <param name="qual">Que dragao -- ver <see cref="RetratoDoDragaoDe"/>.</param>
	private MenuDoDragao MandarOMenuDoDragao(ServerPlayer pl, bool abrir, string foco = "",
											 QualDragao qual = QualDragao.Nenhum)
	{
		if (!_olhandoODragao.TryGetValue(pl.Id, out OlharNoDragao? olhar))
			_olhandoODragao[pl.Id] = olhar = new OlharNoDragao();
		olhar.Qual = qual;

		RetratoDoDragao r = RetratoDoDragaoDe(pl, qual);
		r.Menu.Abrir = abrir;
		r.Menu.Foco = foco;
		Despachar(pl, r, olhar);
		return r.Menu;
	}

	/// <summary>
	/// **ACERTA AS JANELAS ABERTAS** -- uma vez por segundo (o item 6 do <see cref="TickDasEsferas"/>) e logo
	/// depois de cada verbo que mexe no que elas mostram (`db_desejar`, `sdb_pedido`, ...).
	///
	/// PRA CADA UM QUE ESTA OLHANDO, uma de tres coisas:
	///   * o dragao que ele olhava FOI EMBORA (a invocacao daquele set acabou; as sete Super se espalharam):
	///     a janela fecha, e ele deixa de estar olhando;
	///   * o menu dele MUDOU (a assinatura e outra): sai o menu novo, sem pedir pra abrir nada;
	///   * nada mudou: nao sai nada.
	///
	/// QUEM SAIU DO MUNDO some da lista aqui, sem gancho no logout: o id nao esta mais em `_players`.
	/// </summary>
	private void SincronizarOsMenusDoDragao()
	{
		if (_olhandoODragao.Count == 0) return;

		foreach ((int id, OlharNoDragao olhar) in _olhandoODragao.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _olhandoODragao.Remove(id); continue; }

			// ============================ A JANELA DE UM NAO DERRUBA O TIQUE DE TODOS ============================
			// Isto roda dentro do tique de segundo das esferas (e dele saem a policia de planeta, a divida do
			// desejo supremo, o prazo do dragao). Uma excecao montando o menu de UM jogador pararia o laco e os
			// outros ficariam com a janela velha; solta, ela se repetiria a cada segundo. Ela vira um aviso no
			// log, e aquele jogador deixa de ser acertado -- o botao da aba World pede a janela de novo.
			// =====================================================================================================
			try
			{
				bool sumiu = olhar.Dragao switch
				{
					QualDragao.Comum => olhar.DragaoDePe && !_invocacoes.ContainsKey(olhar.Set),
					QualDragao.Super => olhar.Ciclo != _cicloDasSupers,
					_ => false,
				};

				if (sumiu && !DragaoSomeCaladoDeTeste)
				{
					_olhandoODragao.Remove(id);
					Enviar(pl, new MenuDoDragao { Fechar = true, Dragao = olhar.Dragao });
					continue;
				}

				RetratoDoDragao r = RetratoDoDragaoDe(pl, olhar.Qual);
				if (r.Menu.Assinatura() == olhar.Assinatura && !MenuSaiTodoTiqueDeTeste) continue;
				Despachar(pl, r, olhar);
			}
			catch (Exception e)
			{
				_olhandoODragao.Remove(id);
				Godot.GD.PushWarning($"[server] menu do dragao de {pl.Name} nao pode ser montado: {e}");
			}
		}
	}

	/// <summary>Manda um menu e anota o que foi mandado, pra proxima sincronia comparar.</summary>
	private void Despachar(ServerPlayer pl, RetratoDoDragao r, OlharNoDragao olhar)
	{
		olhar.Assinatura = r.Menu.Assinatura();
		olhar.Dragao = r.Menu.Dragao;
		olhar.Set = r.Set;
		olhar.DragaoDePe = r.DragaoDePe;
		olhar.Ciclo = _cicloDasSupers;
		Enviar(pl, r.Menu);
	}

	private void Enviar(ServerPlayer pl, MenuDoDragao m)
	{
		var w = Protocol.Begin(Protocol.S2C.MenuDoDragao);
		FioDoMenuDoDragao.Escrever(w, m);
		EscutaDosMenusDoDragao?.Add((pl.Id, m, w.CopyData()));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// QUE MENU ESTE JOGADOR VE
	// =====================================================================
	/// <summary>
	/// Um menu e o que o servidor precisa lembrar dele pra saber, depois, se o dragao foi embora. Os dois
	/// ultimos campos nao viajam: sao do <see cref="OlharNoDragao"/>.
	/// </summary>
	private readonly record struct RetratoDoDragao(MenuDoDragao Menu, int Set, bool DragaoDePe);

	/// <summary>
	/// **O MENU QUE ESTE JOGADOR VE AGORA**, ja na ordem em que a janela o desenha. O funil unico: quem manda
	/// (<see cref="MandarOMenuDoDragao"/>), quem acerta (<see cref="SincronizarOsMenusDoDragao"/>) e a bancada
	/// leem daqui.
	/// </summary>
	private RetratoDoDragao RetratoDoDragaoDe(ServerPlayer pl, QualDragao qual)
	{
		RetratoDoDragao r = QueDragaoEsteJogadorVe(pl, qual);
		PorNaOrdemDaJanela(r.Menu);
		return r;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--menudodragaoteste`): a lista sai na ordem da tabela do DM -- a janela abre
	/// em "Nada (desperdiçar o pedido)" e numa "Calcinha" apagada, antes de qualquer desejo que de pra pedir.
	/// Falso em jogo, sempre.
	/// </summary>
	internal bool ListaNaOrdemDaTabelaDeTeste;

	/// <summary>
	/// **A ORDEM DA JANELA**: primeiro o que da pra pedir, depois o que nao da, os gestos por ultimo.
	///
	/// A tabela vem na ordem do `WishTable.dm`, que e a escada de poder ("Nothing", "Panties", "Cancel",
	/// "Cash"...). Nessa ordem a janela abria com "Nada (desperdiçar o pedido)" e uma "Calcinha" apagada, com
	/// tres linhas de motivo em vermelho -- antes de qualquer desejo que alguem queira. Quem acabou de invocar
	/// o dragao le primeiro o que PODE pedir; o apagado, com o motivo, fica embaixo pra quem for procurar.
	///
	/// DENTRO DE CADA GRUPO a ordem continua a da tabela (o `OrderBy` e estavel), com uma excecao: "nada" fecha
	/// o grupo dos pediveis -- desperdicar o pedido e a ultima opcao, e nao a primeira. A lista toda apagada
	/// (ninguem invocou; foi outro quem invocou) sai na ordem da tabela, intacta.
	/// </summary>
	private void PorNaOrdemDaJanela(MenuDoDragao m)
	{
		if (ListaNaOrdemDaTabelaDeTeste) return;

		List<EntradaDoDragao> ordenadas = [.. m.Entradas.OrderBy(e => e.Acao ? 3 : !e.Pode ? 2 : e.Id == "nada" ? 1 : 0)];
		m.Entradas.Clear();
		m.Entradas.AddRange(ordenadas);
	}

	/// <summary>
	/// QUE DRAGAO ESTE JOGADOR VE. Em ordem:
	///
	///   1. O DRAGAO QUE ELE INVOCOU, de pe -- esteja ele onde estiver. E o unico menu em que da pra pedir ao
	///      dragao comum (so quem invocou pede, ver <see cref="PedirDesejo"/>), e por isso passa na frente.
	///   2. O SUPER SHENRON, se ele pediu esse (<see cref="QualDragao.Super"/>) ou esta no espaco: as Super
	///      Esferas nao pertencem a planeta nenhum.
	///   3. O DRAGAO DA ESTATUA DESTE MUNDO: a lista do que ele atende, apagada, com a frase de por que nao da
	///      pra pedir agora (ninguem o invocou; foi outro quem invocou; as sete ainda dormem).
	///   4. NENHUM: nao ha estatua aqui.
	/// </summary>
	private RetratoDoDragao QueDragaoEsteJogadorVe(ServerPlayer pl, QualDragao qual)
	{
		if (qual != QualDragao.Super
			&& _invocacoes.Values.FirstOrDefault(i => i.Invocador == pl.Id) is { } minha
			&& SetPorId(minha.Set) is { } meuSet)
			return new RetratoDoDragao(MenuDoDragaoComum(pl, meuSet, minha), meuSet.Id, true);

		if (qual == QualDragao.Super || (qual == QualDragao.Nenhum && Espaco.EhEspaco(pl.Zone)))
			return new RetratoDoDragao(MenuDoSuperShenron(pl), -1, false);

		if (_sets.Find(x => x.Zona.Hash == pl.Zone.Hash) is { } daqui)
			return new RetratoDoDragao(MenuDoDragaoComum(pl, daqui, null), daqui.Id, _invocacoes.ContainsKey(daqui.Id));

		var vazio = new MenuDoDragao
		{
			Dragao = QualDragao.Nenhum,
			Nome = "NENHUM DRAGÃO",
			Estado = "Não há Estátua do Dragão neste mundo -- e sem estátua não há esferas. Só um Namekuseijin "
				   + "do Clã do Dragão ergue uma, e só num mundo que ele domine (ou na Terra, se for o Guardião).",
		};
		PorOAtalhoDoSuper(pl, vazio);
		return new RetratoDoDragao(vazio, -1, false);
	}

	// =====================================================================
	// O DRAGAO COMUM -- Shenron, Porunga
	// =====================================================================
	/// <summary>O rotulo da resposta que e o proprio jogador. As bancadas e a janela a acham por ele.</summary>
	internal const string ParaMim = "Para mim";

	/// <param name="minha">A invocacao DESTE jogador neste set, ou nula (ele so esta olhando).</param>
	private MenuDoDragao MenuDoDragaoComum(ServerPlayer pl, SetDeEsferas s, Invocacao? minha)
	{
		Invocacao? dePe = _invocacoes.GetValueOrDefault(s.Id);
		bool meu = minha != null;
		bool posso = meu && s.Pedidos < s.Desejos;

		var m = new MenuDoDragao
		{
			Dragao = QualDragao.Comum,
			Nome = s.NomeDoDragao,
			Titulo = s.TituloDoDragao,
			DePe = posso,
			Restam = Math.Max(0, s.Desejos - s.Pedidos),
			Total = s.Desejos,
			Segundos = meu ? (int)Math.Clamp(Math.Ceiling(minha!.ExpiraEm - TempoDoMundo), 0, ushort.MaxValue) : 0,
			Estado = meu ? $"{s.NomeDoDragao} aguarda o seu pedido."
				   : dePe != null ? $"{s.NomeDoDragao} está de pé diante de {NomeDoCorpo(dePe.Invocador)}: só quem invocou pede."
				   : s.Inerte ? "As esferas deste mundo estão INERTES: só quem as criou pode despertá-las. Esta é a lista "
							  + "do que o dragão delas atende."
				   : TempoDoMundo < s.AtivoEm ? $"As esferas ainda se refazem: faltam {(s.AtivoEm - TempoDoMundo) / 3600.0:0.#} h reais. "
											  + $"Esta é a lista do que {s.NomeDoDragao} atende."
				   : $"{s.NomeDoDragao} não está de pé. Reúna as sete esferas deste mundo e invoque -- esta é a "
				   + "lista do que ele atende.",
		};

		// ============================ "PRA MIM OU PRA OUTRA PESSOA" E UMA LINHA SO ============================
		// A tabela do DM tem "Youth" e "Make Somebody Else Young" como dois desejos (`WishTable.dm:296` e
		// `:275`), e os dois continuam existindo no verbo. Na janela eles sao UMA linha, "Juventude", cuja
		// segunda etapa pergunta pra quem -- que e o pedido do dono ao pe da letra. Sem "Juventude" na lista
		// (nao acontece: ela entra no patamar 3 e a alheia no 4) a alheia apareceria sozinha.
		// ======================================================================================================
		List<DesejoDef> lista = [.. MenuDaqui(pl, s)];
		bool juntas = lista.Exists(d => d.Id == "juventude") && lista.Exists(d => d.Id == "juventude_alheia");

		foreach (DesejoDef d in lista)
		{
			if (d.Id == "cancelar") continue;                      // e o gesto "Dispensar o dragao", la embaixo
			if (d.Id == "juventude_alheia" && juntas) continue;    // mora dentro de "Juventude"
			m.Entradas.Add(EntradaDoDesejo(pl, s, d, posso, juntas));
		}

		// ---------------------------------------------------------- os gestos em volta do dragao
		if (meu)
			m.Entradas.Add(new EntradaDoDragao
			{
				Id = AcoesDoDragao.Dispensar, Acao = true, Verbo = "db_desejar", Pedido = "cancelar",
				Nome = "Dispensar o dragão",
				Desc = $"{s.NomeDoDragao} some no céu sem gastar pedido. O que resta desta ativação continua "
					 + "valendo pra próxima invocação.",
			});

		// `Redo()` (`Dragonballs.dm:59-89`): so quem ergueu, e nunca a ancestral -- e so no mundo dela, porque o
		// `RefazerOSet` reconfigura a estatua do mundo em que se ESTA (quem invocou e foi pra outro planeta ve
		// aqui o dragao de la, e o gesto mexeria na estatua de ca). A pergunta e o FATO que a tabela de
		// requisitos da aos botoes do criador (`Fatos.CriouAEstatuaDaqui`, o de "Despertar as esferas"): uma
		// regra so pra aba World e pra esta janela.
		bool ergueuAEstatuaDaqui = GestoDeOutroMundoDeTeste
			? s.CriadorSig.Length > 0 && string.Equals(s.CriadorSig, pl.Assinatura, StringComparison.Ordinal)
			: s.Zona.Hash == pl.Zone.Hash && (FatosDe(pl) & Jandirus.Core.Verbos.Fatos.CriouAEstatuaDaqui) != 0;
		if (!s.Eterno && ergueuAEstatuaDaqui)
		{
			var pedidos = new EntradaDoDragao
			{
				Id = AcoesDoDragao.Pedidos, Acao = true, Perigo = true, Verbo = "db_refazer",
				Nome = $"Pedidos por invocação (hoje: {s.Desejos})",
				Desc = "Só quem ergueu a estátua. Ela passa a valer com o SEU poder de agora, e as sete se "
					 + "espalham de novo e voltam a dormir a espera de nascimento.",
				Pergunta = "Quantos pedidos por invocação?",
			};
			for (int n = 1; n <= 3; n++)
				pedidos.Escolhas.Add(new EscolhaDoDragao
				{
					Pedido = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
					Rotulo = n == 1 ? "1 pedido" : $"{n} pedidos",
					Nota = n switch
					{
						1 => "cada pedido com a força inteira do set",
						2 => "dois pedidos mais fracos; ainda dá Ressuscitar TODOS e Matar",
						_ => "três ainda mais fracos; o set PERDE Ressuscitar TODOS e Matar",
					},
				});
			m.Entradas.Add(pedidos);
		}

		// `Destroy_Statue()` (`:50-58`): qualquer um AO LADO dela, e nunca a ancestral -- a mesma pergunta do
		// `DerrubarAEstatua` (`EstatuaAoAlcance`).
		if (EstatuaAoAlcance(pl) is { Eterno: false } aoLado && aoLado.Id == s.Id)
			m.Entradas.Add(new EntradaDoDragao
			{
				Id = AcoesDoDragao.Derrubar, Acao = true, Perigo = true, Verbo = "db_derrubar", Pedido = "sim",
				Nome = "Derrubar a estátua",
				Desc = "APAGA as sete esferas deste mundo, para sempre. Qualquer um ao lado da estátua pode -- "
					 + "e não tem volta.",
			});

		PorOAtalhoDoSuper(pl, m);
		return m;
	}

	/// <summary>
	/// UMA LINHA DE DESEJO do dragao comum: pode ou nao pode (e por que), e o que a segunda etapa pergunta.
	/// </summary>
	/// <param name="posso">O dragao esta de pe e e deste jogador. Falso = a lista e so pra ler.</param>
	/// <param name="juntas">"Juventude" carrega tambem a de outra pessoa -- ver o chamador.</param>
	private EntradaDoDragao EntradaDoDesejo(ServerPlayer pl, SetDeEsferas s, DesejoDef d, bool posso, bool juntas)
	{
		var e = new EntradaDoDragao
		{
			Id = d.Id, Verbo = "db_desejar", Pedido = d.Id, Nome = d.Nome, Desc = d.Desc,
			// O DESEJO QUE COBRA A VIDA vem em vermelho, como o do Super Shenron: um ano depois ele mata, e dessa
			// morte nao ha volta (`sw_strongest_wish`, `WishTable.dm:102-113`).
			Perigo = d.Id == "supremo",
		};

		// O NAO-PORTADO APARECE APAGADO E DIZ POR QUE, com dragao ou sem: e o `DesejoDef.Falta`, o motivo que
		// o `PedirDesejo` ja dava a quem digitava o id.
		if (!d.Portado)
		{
			e.Pode = false;
			e.Motivo = $"Ainda não foi portado deste jogo: {d.Falta}.";
			return e;
		}

		// SEM DRAGAO PRA PEDIR, a linha e so leitura. O motivo e um so pra lista inteira e mora no alto da
		// janela (`MenuDoDragao.Estado`): repeti-lo em vinte linhas seria vinte vezes a mesma frase.
		if (!posso) { e.Pode = false; return e; }

		if (!MenuNaoApagaNadaDeTeste)
		{
			// O CRIADOR QUE PEDE PODER PERDE A INVOCACAO INTEIRA (`WishTable.dm:199` e `:207`): a linha
			// apagada e o que o impede de descobrir isso gastando o dragao.
			if (OCriadorNaoPede(pl, s, d))
			{
				e.Pode = false;
				e.Motivo = "As esferas usam o SEU poder: quem criou o set não se amplifica com elas -- e pedir "
						 + "isto encerraria a invocação.";
				return e;
			}

			if (d.Id == "marcos" && JaPediuMarcos(pl))
			{
				e.Pode = false;
				e.Motivo = "Você já pediu Marcos ao dragão uma vez -- e é uma vez por vida.";
				return e;
			}

			if (d.Id == "reviver_todos" && MortosQueVoltam().Count == 0)
			{
				e.Pode = false;
				e.Motivo = "Não há ninguém para trazer de volta.";
				return e;
			}
		}

		// ---------------------------------------------------------- "pra mim ou pra outra pessoa?"
		if (d.Id == "juventude")
		{
			e.Pergunta = juntas ? "Juventude pra quem?" : "Voltar a que idade?";
			if (juntas) e.Desc = "O corpo volta aos 25 anos, o auge -- o seu, ou o de outra pessoa ao alcance do dragão.";

			e.Escolhas.Add(new EscolhaDoDragao
			{
				Pedido = "juventude", Rotulo = ParaMim,
				Nota = $"{Desejos.IdadeDaJuventude} anos, o auge",
			});
			e.Escolhas.Add(new EscolhaDoDragao
			{
				Pedido = "juventude extremo", Rotulo = $"{ParaMim}, extremamente jovem",
				Nota = $"{Desejos.IdadeDaJuventudeExtrema} anos -- e um corpo de criança carrega 9% menos poder",
			});

			if (juntas)
			{
				int antes = e.Escolhas.Count;
				PorPessoas(e, pl, "juventude_alheia", VivosAoAlcanceDoDragao(pl));
				if (e.Escolhas.Count == antes)
					e.Escolhas.Add(new EscolhaDoDragao
					{
						Rotulo = "Outra pessoa", Pode = false,
						Nota = "ninguém ao alcance do dragão: ela precisa estar ao seu lado",
					});
			}
			return e;
		}

		// ---------------------------------------------------------- o que o desejo alcanca (`DesejoDef.Alvo`)
		switch (d.Alvo)
		{
			case AlvoDoDesejo.Vivo:
				e.Pergunta = d.Id == "matar" ? "Matar quem?" : "Pra quem?";
				PorPessoas(e, pl, d.Id, VivosAoAlcanceDoDragao(pl));
				if (e.Escolhas.Count == 0)
				{
					e.Pode = false;
					e.Motivo = "Ninguém ao alcance do dragão: quem o desejo alcança precisa estar ao seu lado.";
				}
				break;

			case AlvoDoDesejo.Morto:
			{
				e.Pergunta = "Trazer quem de volta?";
				List<ServerPlayer> mortos = MortosQueVoltam();

				// "PARA MIM" SO VALE PRA UM MORTO: quem invocou e morreu com o dragao de pe pede a propria
				// vida. Vivo, a resposta aparece apagada e diz por que -- e a pergunta do dono, inteira.
				bool eu = mortos.Contains(pl);
				e.Escolhas.Add(new EscolhaDoDragao
				{
					Pedido = eu ? $"{d.Id} #{pl.Id}" : "", Rotulo = ParaMim, Pode = eu,
					Nota = eu ? "você volta à vida" : "você está vivo",
				});
				PorPessoas(e, pl, d.Id, [.. mortos.Where(o => o != pl)]);

				if (!e.Escolhas.Exists(c => c.Pode))
				{
					e.Pode = false;
					e.Motivo = "Não há ninguém para trazer de volta (quem morreu de VELHICE não volta por desejo nenhum).";
				}
				break;
			}

			case AlvoDoDesejo.Planeta:
				e.Pergunta = "Qual mundo volta a existir?";
				foreach ((string chave, string nome, _) in MundosMortos().Take(FioDoMenuDoDragao.MaxEscolhas))
					e.Escolhas.Add(new EscolhaDoDragao
					{
						Pedido = $"{d.Id} {chave}", Rotulo = NomeDoPlaneta(nome), Nota = "destruído",
					});
				if (e.Escolhas.Count == 0) { e.Pode = false; e.Motivo = "Nenhum mundo foi destruído."; }
				break;

			case AlvoDoDesejo.Saiyajin:
				e.Pergunta = "O corpo de qual Saiyajin?";
				PorPessoas(e, pl, d.Id, SaiyajinsDeMolde(pl));
				if (e.Escolhas.Count == 0)
				{
					e.Pode = false;
					e.Motivo = "Nenhum Saiyajin vivo no mundo para servir de molde.";
				}
				break;
		}
		return e;
	}

	/// <summary>
	/// PoE AS PESSOAS DE UMA LISTA como respostas da segunda etapa: o alvo MARCADO de quem pede na frente (o
	/// duplo clique que ele ja usa pra tudo), o resto por nome.
	/// </summary>
	/// <param name="porNome">
	/// O argumento leva o NOME em vez do `#id`. So o pedido REGISTRADO do Super Shenron pede isso: ele vai pro
	/// disco (`superesferas.json`) e e pronunciado depois, e id de sessao nao atravessa reinicio.
	/// </param>
	private void PorPessoas(EntradaDoDragao e, ServerPlayer pl, string idDoDesejo, List<ServerPlayer> lista,
							bool porNome = false)
	{
		foreach (ServerPlayer o in lista.OrderByDescending(o => o.Id == pl.AlvoId)
										.ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
		{
			if (e.Escolhas.Count >= FioDoMenuDoDragao.MaxEscolhas) break;
			bool marcado = o.Id == pl.AlvoId;
			e.Escolhas.Add(new EscolhaDoDragao
			{
				Pedido = $"{idDoDesejo} {(porNome ? o.Name : "#" + o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))}".Trim(),
				Rotulo = o.Name,
				Nota = Jandirus.Core.Races.NomeDaRaca.Bonito(o.Race) + (marcado ? " · seu alvo marcado" : ""),
				Marcado = marcado,
			});
		}
	}

	// =====================================================================
	// O SUPER SHENRON
	// =====================================================================
	/// <summary>Este jogador tem o que resolver com o Super Shenron, esteja onde estiver?</summary>
	private bool TemAssuntoComOSuper(ServerPlayer pl) =>
		QuantasSupersTem(pl.Assinatura) >= SuperEsferas.Total
		|| (HaProcuracao && string.Equals(_benefSig, pl.Assinatura, StringComparison.Ordinal))
		|| PrecoEsperandoPor(pl)
		|| OfertaDeGuardaPara(pl) != null;

	/// <summary>O desejo supremo espera a palavra DESTE jogador, e o prazo nao venceu (`_precoPendente`).</summary>
	private bool PrecoEsperandoPor(ServerPlayer pl) =>
		!string.IsNullOrEmpty(_precoPendente.Conta)
		&& string.Equals(_precoPendente.Conta, pl.Conta, StringComparison.OrdinalIgnoreCase)
		&& NowMs() <= _precoPendente.Ate;

	/// <summary>A oferta de guarda de pe pra este jogador, ou nula (`_ofertasDeGuarda`).</summary>
	private OfertaDeGuarda? OfertaDeGuardaPara(ServerPlayer pl) =>
		pl.Conta.Length > 0 && _ofertasDeGuarda.TryGetValue(pl.Conta, out OfertaDeGuarda? o) && NowMs() <= o.Ate
			? o : null;

	/// <summary>
	/// QUEM TEM ASSUNTO COM O SUPER SHENRON E ESTA NUM PLANETA acha a porta dele dentro do menu que abriu: a
	/// aba World de um planeta abre o dragao DAQUELE mundo, e o pedinte que emprestou as sete nao precisa voar
	/// ate o espaco pra escolher o pedido.
	/// </summary>
	private void PorOAtalhoDoSuper(ServerPlayer pl, MenuDoDragao m)
	{
		if (!TemAssuntoComOSuper(pl)) return;
		m.Entradas.Add(new EntradaDoDragao
		{
			Id = AcoesDoDragao.Super, Acao = true, Direto = true, Verbo = "db_menu", Pedido = "super",
			Nome = "Super Shenron",
			Desc = "Você tem assunto com ele: abre o menu das Super Esferas.",
		});
	}

	/// <summary>
	/// O MENU DO SUPER SHENRON -- as cinco situacoes do `Invocar_Super_Shenron` e do procurador
	/// (`ProceduralSpace.dm:1578-1680`), cada uma com a frase do alto e as linhas que cabem nela:
	///
	///   * CARREGO A GUARDA DE OUTRO: uma linha so, "PRONUNCIAR" -- o falante nao escolhe (o `sdb_invocar`
	///     dele ignora argumento, ver <see cref="ChamarOSuperShenron"/>);
	///   * EMPRESTEI AS SETE: os desejos, e escolher um e REGISTRAR (`sdb_pedido`); mais "Retomar a guarda";
	///   * TENHO AS SETE E FALO: os desejos, e escolher um e pedir (`sdb_invocar`); mais "Emprestar as sete";
	///   * TENHO AS SETE E NAO FALO: os desejos apagados, a frase que ensina o procurador, e "Emprestar";
	///   * NAO TENHO AS SETE: os desejos apagados e quantas faltam.
	///
	/// E, EM QUALQUER UMA, o que espera a palavra deste jogador: o preco do desejo supremo e a guarda que lhe
	/// ofereceram -- os dois `alert()` do DM (`:1632` e `:1667`).
	/// </summary>
	private MenuDoDragao MenuDoSuperShenron(ServerPlayer pl)
	{
		// A LINGUA SE REAVALIA ANTES DE RESPONDER, como no `ChamarOSuperShenron` (`godtongue_check`, `:1587`):
		// o Kai recem-coroado que nao relogou nao pode ler "voce nao fala" numa janela.
		ConferirALingua(pl);

		int minhas = QuantasSupersTem(pl.Assinatura);
		bool sete = minhas >= SuperEsferas.Total;
		bool souOPedinte = HaProcuracao && string.Equals(_benefSig, pl.Assinatura, StringComparison.Ordinal);
		bool carregoProcuracao = sete && HaProcuracao && !souOPedinte;
		bool falo = pl.Ficha.godtongue;
		bool emCondicoes = !pl.Ficha.dead && !pl.Ficha.KO;
		// O LUGAR, pela mesma pergunta do verbo (`set src in oview(2)`, `:1580`): a janela abre em qualquer canto
		// (quem emprestou escolhe o pedido de dentro de um planeta), mas CHAMAR o dragao e so ao lado de uma esfera.
		bool diante = DianteDeUmaSuper(pl);
		const string vaAteUma = "O Super Shenron só é chamado AO LADO de uma Super Esfera: vá até uma delas, no espaço.";

		var m = new MenuDoDragao
		{
			Dragao = QualDragao.Super,
			Nome = "SUPER SHENRON",
			Titulo = "O DEUS DRAGÃO",
			Restam = 1,
			Total = 1,
		};

		if (carregoProcuracao)
		{
			List<DesejoDef> doPedinte = [.. Desejos.MenuDoSuper(EhKaioshinDoCorpo(pl), haProcuracao: true)];
			DesejoDef? registrado = doPedinte.Find(d => d.Id == _pedidoDoBenef);
			bool pedinteNoMundo = Pessoas.Any(p => string.Equals(p.Assinatura, _benefSig, StringComparison.Ordinal));

			// AS RECUSAS DO `ChamarOSuperShenron`, na ordem em que ele as da.
			string motivo = !emCondicoes ? "Você não está em condições de chamar coisa nenhuma."
						  : !diante ? vaAteUma
						  : !falo ? "Você não fala a Língua dos Deuses."
						  : !pedinteNoMundo ? $"{_benefNome} precisa estar no mundo para receber o desejo."
						  : _pedidoDoBenef.Length == 0 ? $"{_benefNome} ainda não disse o que pedir. Só ele(a) escolhe."
						  : registrado == null ? $"O pedido registrado não é dos que o Super Shenron atende agora: {_benefNome} precisa escolher outro."
						  : "";

			m.DePe = motivo.Length == 0;
			m.Estado = $"Você carrega a guarda das sete em nome de {_benefNome}. O desejo é dele(a): você só pronuncia.";
			m.Entradas.Add(new EntradaDoDragao
			{
				Id = AcoesDoDragao.Pronunciar, Acao = true, Verbo = "sdb_invocar", Pedido = "",
				Nome = registrado != null
					? $"PRONUNCIAR: {registrado.Nome}" + (_alvoDoPedido.Length > 0 ? $" ({_alvoDoPedido})" : "")
					: $"PRONUNCIAR o pedido de {_benefNome}",
				Desc = $"O que {_benefNome} escolheu. Dito, as sete se espalham de novo pela galáxia.",
				Pode = motivo.Length == 0,
				Motivo = motivo,
			});
		}
		else if (souOPedinte)
		{
			string comQuem = _supers.FirstOrDefault(x => x.Dono.Length > 0)?.DonoNome ?? "outra pessoa";
			m.DePe = true;
			m.Estado = $"Você emprestou as sete a {comQuem}. O desejo é SEU: escolha o pedido, e ele(a) só pronuncia"
					 + (_pedidoDoBenef.Length > 0 ? " -- dá pra trocar até a hora em que for dito." : ".");

			foreach (DesejoDef d in Desejos.MenuDoSuper(ehKaioshin: false, haProcuracao: true))
				m.Entradas.Add(EntradaDoSuper(pl, d, "sdb_pedido", posso: true, registrando: true));

			m.Entradas.Add(new EntradaDoDragao
			{
				Id = AcoesDoDragao.Retomar, Acao = true, Verbo = "sdb_revogar",
				Nome = "Retomar a guarda das sete",
				Desc = "As sete voltam pra você na hora, e a procuração acaba.",
			});
		}
		else if (sete)
		{
			bool posso = falo && emCondicoes && diante;
			m.DePe = posso;
			m.Estado = !emCondicoes ? "Você não está em condições de chamar coisa nenhuma."
					 : !falo ? "As sete são suas, mas só a LÍNGUA DOS DEUSES desperta o Super Shenron: cargos divinos a "
					 + "conhecem, e o sangue Kai ou Demigod nasce com ela. Empreste as sete a quem fale -- o desejo "
					 + "continua sendo seu."
					 : !diante ? "As sete Super Esferas são suas, e você fala a Língua dos Deuses. " + vaAteUma
					 : "As sete Super Esferas são suas, e você fala a Língua dos Deuses. SUPER SHENRON atende UM desejo.";

			foreach (DesejoDef d in Desejos.MenuDoSuper(EhKaioshinDoCorpo(pl), haProcuracao: false))
				m.Entradas.Add(EntradaDoSuper(pl, d, "sdb_invocar", posso, registrando: false));

			m.Entradas.Add(EntradaDeEmprestar(pl));
		}
		else
		{
			m.Estado = $"Você reivindicou {minhas} de {SuperEsferas.Total} Super Esferas. O dragão exige TODAS -- "
					 + "esta é a lista do que ele atende.";
			foreach (DesejoDef d in Desejos.MenuDoSuper(EhKaioshinDoCorpo(pl), haProcuracao: false))
				m.Entradas.Add(EntradaDoSuper(pl, d, "sdb_invocar", posso: false, registrando: false));
		}

		// ---------------------------------------------------------- o que espera a MINHA palavra
		if (PrecoEsperandoPor(pl))
		{
			m.DePe = true;
			m.Segundos = (int)Math.Clamp((_precoPendente.Ate - NowMs() + 999) / 1000, 0, ushort.MaxValue);
			m.Entradas.Add(new EntradaDoDragao
			{
				Id = AcoesDoDragao.Preco, Acao = true, Perigo = true, Verbo = "sdb_aceito", Pedido = "ACEITO O PREÇO",
				Nome = "ACEITO O PREÇO",
				Desc = "Trocar TODO o seu tempo de vida por poder absoluto: o DOBRO do maior poder do jogo, e SÓ "
					 + "MAIS UM ANO de vida. Dessa morte não há revive -- apenas reencarnação.",
			});
			m.Entradas.Add(new EntradaDoDragao
			{
				Id = AcoesDoDragao.RecusarPreco, Acao = true, Direto = true, Verbo = "sdb_aceito", Pedido = "recuso",
				Nome = "Recusar o preço",
				Desc = "Nada acontece: as sete continuam com quem as carrega.",
			});
		}

		if (OfertaDeGuardaPara(pl) is { } oferta)
		{
			m.DePe = true;
			if (m.Segundos == 0)
				m.Segundos = (int)Math.Clamp((oferta.Ate - NowMs() + 999) / 1000, 0, ushort.MaxValue);
			m.Entradas.Add(new EntradaDoDragao
			{
				Id = AcoesDoDragao.AceitarGuarda, Acao = true, Verbo = "sdb_guarda_aceitar",
				Nome = $"Aceitar a guarda das sete de {oferta.DeNome}",
				Desc = $"Você passa a carregar as sete e a falar com o dragão EM NOME de {oferta.DeNome}: o desejo "
					 + "é de quem emprestou, e você só pronuncia.",
			});
			m.Entradas.Add(new EntradaDoDragao
			{
				Id = AcoesDoDragao.RecusarGuarda, Acao = true, Direto = true, Verbo = "sdb_guarda_recusar",
				Nome = "Recusar a guarda",
				Desc = $"As sete ficam com {oferta.DeNome}.",
			});
		}

		return m;
	}

	/// <summary>UMA LINHA DE DESEJO do Super Shenron.</summary>
	/// <param name="verbo">`sdb_invocar` (quem fala pede) ou `sdb_pedido` (quem emprestou registra).</param>
	/// <param name="registrando">O pedido vai ser GUARDADO e dito depois: o alvo viaja pelo nome.</param>
	private EntradaDoDragao EntradaDoSuper(ServerPlayer pl, DesejoDef d, string verbo, bool posso, bool registrando)
	{
		var e = new EntradaDoDragao
		{
			Id = d.Id, Verbo = verbo, Pedido = d.Id, Nome = d.Nome, Desc = d.Desc,
			Perigo = d.Id == "sdb_supremo",
		};
		if (!posso) { e.Pode = false; return e; }

		if (registrando && d.Id == _pedidoDoBenef)
			e.Motivo = "É o pedido registrado agora" + (_alvoDoPedido.Length > 0 ? $": {_alvoDoPedido}." : ".");

		switch (d.Alvo)
		{
			case AlvoDoDesejo.Morto:
				e.Pergunta = "Trazer quem de volta?";
				PorPessoas(e, pl, d.Id, MortosQueVoltam(), porNome: registrando);
				if (e.Escolhas.Count == 0)
				{
					e.Pode = false;
					e.Motivo = "Nenhum guerreiro caído que possa voltar (a morte de VELHICE nem o Super Shenron reverte).";
				}
				break;

			case AlvoDoDesejo.Saiyajin:
				e.Pergunta = "O corpo de qual Saiyajin?";
				PorPessoas(e, pl, d.Id, SaiyajinsDeMolde(pl), porNome: registrando);
				if (e.Escolhas.Count == 0)
				{
					e.Pode = false;
					e.Motivo = "Nenhum Saiyajin vivo no mundo para servir de molde.";
				}
				break;
		}
		return e;
	}

	/// <summary>
	/// "EMPRESTAR AS SETE" -- o `Transferir_Super_Esferas` (`:1651-1680`) com a lista de quem esta ao lado, que
	/// e a `oview(1)` do DM e a mesma que o <see cref="TransferirAGuarda"/> confere.
	/// </summary>
	private EntradaDoDragao EntradaDeEmprestar(ServerPlayer pl)
	{
		var e = new EntradaDoDragao
		{
			Id = AcoesDoDragao.Emprestar, Acao = true, Verbo = "sdb_transferir",
			Nome = "Emprestar as sete",
			Desc = "Confia as sete a alguém AO SEU LADO, pra falar com o dragão em seu nome. O desejo continua "
				 + "sendo seu -- você escolhe, ele só pronuncia -- e dá pra retomar a qualquer momento.",
			Pergunta = "Emprestar as sete a quem?",
		};

		if (pl.Ficha.dead) { e.Pode = false; e.Motivo = "Um morto não confia esferas a ninguém."; return e; }

		foreach (ServerPlayer o in QuemPodeGuardarAsSete(pl).OrderByDescending(o => o.Id == pl.AlvoId)
															.ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
															.Take(FioDoMenuDoDragao.MaxEscolhas))
		{
			// SEM ENSINAR A LINGUA A NINGUEM so por aparecer numa lista: a pergunta e a do `godtongue_check`
			// em modo quieto (`:1666`), que o `TransferirAGuarda` faz de verdade em quem for escolhido.
			bool fala = o.Ficha.godtongue
					 || LinguaDosDeuses.Reavaliar(false, CargoDe(o.Conta), o.Race, o.Ficha.ParentRace);
			e.Escolhas.Add(new EscolhaDoDragao
			{
				Pedido = "#" + o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
				Rotulo = o.Name,
				Nota = fala ? "fala a Língua dos Deuses" : "NÃO fala a Língua dos Deuses: não conseguirá invocar",
				Marcado = o.Id == pl.AlvoId,
			});
		}

		if (e.Escolhas.Count == 0)
		{
			e.Pode = false;
			e.Motivo = "Chame para o seu lado quem vai falar por você: o gesto é presencial.";
		}
		return e;
	}
}
