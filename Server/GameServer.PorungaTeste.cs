using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO SET QUE MORRE COM O PLANETA (`--porungateste`).
///
/// O pedido do dono, literal: *"sim porunga morre em namek quando namek explode, so voltando quando o
/// planeta e restaurado pelas esferas de outro lugar"*.
///
/// ============================ O QUE SO DAQUI SE RESPONDE ============================
///   1. **O PORUNGA MORRE MESMO, PELO COMMIT DE PRODUCAO?** Namek e destruida pelo caminho inteiro
///      (`ComecarDestruicao` -> `TickDaDestruicao` segundo a segundo -> `ConsumarDestruicao`), e o
///      resultado e lido nas LISTAS DE PRODUCAO: nao ha set eterno, e nao ha nenhuma das sete dele.
///      Nada aqui pergunta se uma funcao foi chamada.
///   2. **...E O SET DA TERRA NAO MORRE JUNTO?** A metade que impede a checagem 1 de ficar verde num
///      sistema que simplesmente apaga tudo. Um set ancorado noutro mundo atravessa a explosao com o
///      `Ciclo` e as sete intactos.
///   3. **QUEM ESTAVA COM UMA ESFERA NA MAO PERDE A ESFERA -- E SABE DISSO?** As duas metades no
///      mesmo corpo, no mesmo instante: a esfera do set de Namek some da mao e a do set da Terra
///      fica. E o aviso e lido no que o JOGADOR OUVE (`EscutaDeAvisos`), nao no que o codigo pretende.
///   4. **O REINICIO DESENTERRA?** A porta que quase ficou de fora. Com Namek morta, um `esferas.json`
///      que TRAGA o Porunga (backup restaurado, gravacao perdida) e lido pelo `CarregarEsferas` de
///      producao -- e o set tem que morrer de novo na carga. A outra metade: com Namek viva, a MESMA
///      carga ergue o Porunga.
///   5. **A VOLTA E PELO DESEJO, COM ESFERAS DE OUTRO LUGAR?** A frase do dono, inteira e sem atalho:
///      as sete da TERRA na mao, o dragao de pe pelo verbo, `db_desejar curar_planeta Namek` pelo
///      funil de producao -- e entao Namek existe de novo E o Porunga esta em Namek. **Este desejo
///      nunca tinha sido exercitado por bancada nenhuma** (medido na Fase 0: tres ocorrencias de
///      `curar_planeta` no repo, as tres de producao).
///   6. **O SET DE JOGADOR NAO VOLTA -- E A ZONA FICA LIVRE?** As duas metades da assimetria: o
///      eterno se reconstroi de constantes e volta; o de jogador nao e derivavel de constante nenhuma
///      e nao volta. Mas a zona **desbloqueia**, e outra estatua se ergue ali -- que e exatamente o
///      limbo que a Fase 0 mediu (set inerte, inalcancavel, insalvavel, ocupando o mundo pra sempre).
///   7. **NAO SE ANCORA UM SET NUM MUNDO QUE ESTA ACABANDO?** O portao de ENTRADA, com o mesmo
///      jogador e o mesmo planeta -- so a condenacao mudando. Guardar a saida sem guardar a entrada e
///      o cego que este repo ja nomeou: *"nascer DENTRO do estado nunca testa a ENTRADA nele"*.
///   8. **COM NAMEK VIVA ELE ESTA LA, E ATENDE?** O contra-exemplo sem o qual as checagens 1 e 9
///      ficariam verdes num jogo sem esfera nenhuma: as sete de Namek na mao deste jogador, o
///      `InvocarODragao` de producao, e o PORUNGA de pe. Depois da explosao o gesto e repetido
///      identico -- mesma mao, mesmo verbo -- e nada sobe.
///   9. **ELE NAO VOLTA SOZINHO?** O `ManterOSetEterno` **era quem garantia o oposto**: uma vez por
///      segundo ele curava inercia, poder, desejos, prazo e esfera perdida do eterno. Aqui ele e visto
///      curando tudo isso num tique com Namek VIVA (a metade que impede "o tique nao o trouxe" de
///      ficar verde num tique que nao roda) e depois nao trazendo nada em SESSENTA tiques com Namek
///      morta. E a ordem dentro do tique (enterrar ANTES de manter) e lida no OBJETO: um Porunga
///      forjado num cadaver sai da lista sem o zelador ter chegado a toca-lo.
///  10. **A VOLTA ATRAVESSA O DISCO?** Restaurado o planeta, o Porunga volta -- e o que o jogador
///      encontra amanha e o que sobrevive ao arquivo. `SalvarEsferas` + `CarregarEsferas` de producao
///      depois da restauracao: ele esta la, em Namek, com as sete, os tres desejos, o poder do eterno,
///      pedidos zerados e a espera de nascimento de pe. Sem isto, um portao de carga com a pergunta
///      errada devolveria o Porunga pelo desejo e o comeria no reinicio seguinte, calado.
/// ================================================================================
///
/// ============================ AS DUAS FAMILIAS COM DEFEITO INJETADO ============================
///   A. **A ANCORA MENTINDO** -> o criterio "Namek morta, Porunga enterrado" tem que REPROVAR quando
///      a ancora do set eterno e reescrita pra um mundo vivo. Sem esta familia, o criterio ficaria
///      igualmente verde num sistema que apagasse todo set eterno a cada explosao de qualquer coisa.
///   B. **O PORTAO DO NASCIMENTO** -> o criterio "a carga do disco ergue o Porunga" tem que REPROVAR
///      com Namek no livro dos mortos. E o gate de `ErguerOSetEterno` visto MORDENDO.
/// ==========================================================================================
///
/// ============================ O QUE ELA MEXE, E O QUE PROTEGE CADA COISA ============================
///   * **o disco inteiro** -> `PalcoDeApagamentos`: `_store` passa a apontar pra uma pasta temporaria,
///     entao `esferas.json`, `planetas-mortos.json`, `conquista.json` e o save do testador sao
///     gravados **de verdade, pelo codigo de producao, noutro lugar**. A bancada compara a pasta com a
///     de verdade e afirma que o desvio aconteceu;
///   * **o registro dos mortos em memoria** -> `PalcoDeMortes`: ele devolve o livro, os tremores e o
///     ceu de destruicao dos planetas que morreram aqui dentro, e conta quantas gravacoes barrou;
///   * **os sets, as esferas, os dominios, o trono, a raca/classe/BP do testador e o adianto do ceu**
///     -> fotografados aqui e devolvidos no `finally`.
///
/// **O QUE ELA COBRA DE VERDADE**: Namek e destruida pelo commit, entao os habitantes NPC dela morrem
/// -- eles nao vao pro disco e voltam na proxima manutencao do povoamento. E o preco de medir o
/// caminho de verdade em vez de carimbar a fase a mao, e ele esta escrito aqui pra ninguem descobrir
/// sozinho depois.
/// ==============================================================================================
///
///     Godot --headless --path . --host --rede 7979 --conta bancada_porunga --senha teste
///            --nome PorungaBanca --raca Namekian --porungateste
/// </summary>
public partial class GameServer
{
	private bool _porungaDeTeste;

	/// <summary>Roda uma vez, no primeiro login. MEXE no mundo -- so com a flag.</summary>
	private void RodarBancadaDoPorunga(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DO PORUNGA QUE MORRE COM NAMEK =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ============================ A FOTOGRAFIA ============================
		// O palco de apagamentos protege o DISCO; isto protege a MEMORIA. As duas fazem falta: sem a
		// segunda, quem rodasse a bancada uma vez ficaria com um servidor no ar em que o Porunga esta
		// noutro ciclo e ha uma estatua fantasma em Arlia -- e a proxima gravacao de qualquer sistema
		// levaria isso pro disco de verdade, ja fora do palco.
		// ==================================================================
		var setsGuardados = new List<SetDeEsferas>(_sets);
		var esferasGuardadas = new List<Esfera>(_esferas);
		var dominiosGuardados = new List<Dominio>(_dominios);
		double ceuGuardado = _adiantoDoCeu;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		string racaGuardada = pl.Race, classeGuardada = pl.Class;
		double bpGuardado = pl.Ficha.BP;
		string tronoGuardado = _tronos.GetValueOrDefault("guardian", "");

		var namek = ZoneKey.Premade(Esferas.PlanetaEterno);
		var terra = ZoneKey.Premade("Earth");
		ZoneKey arlia = Cobaia;   // pre-feito, no manifesto, e ninguem nasce nele por padrao

		// AS DUAS ESCUTAS EM VARIAVEL LOCAL, e nao lidas do campo estatico a cada uso: o campo e
		// anulavel e a bancada leria `EscutaDeAvisos!` uma duzia de vezes. A lista e a MESMA (e o
		// objeto que a producao enche), so que com um nome que nao precisa de exclamacao.
		List<string> ouviu = [];
		List<string> anunciou = [];
		EscutaDeAvisos = ouviu;
		EscutaDasEsferas = anunciou;

		try
		{
			// O PALCO DO DISCO POR FORA DE TUDO: ele tem que estar de pe ANTES da primeira gravacao,
			// e a primeira gravacao acontece ja na montagem (o `ErguerEstatua` salva).
			using (PalcoDeApagamentos caixa = PalcoDeApagamentosDeBancada())
			{
				// O `EscritasBarradas` SO E PREENCHIDO NO `Dispose`, entao o palco das mortes nao pode
				// ser um `using`: lendo dentro do escopo o numero sai zero e a checagem vira uma frase.
				// E o mesmo motivo (e a mesma forma) do `--planetateste`.
				PalcoDeMortes palco = PalcoDeMortesDeBancada();
				try
				{
					Checa("(palco) o disco foi desviado pra uma pasta temporaria -- a do dono nao e tocada",
						  _store != null && string.Equals(_store.Pasta, caixa.PastaDeTeste, StringComparison.Ordinal),
						  _store?.Pasta ?? "(sem store)");

					// =====================================================================
					// MONTAGEM -- o estado de partida, medido e nao suposto
					// =====================================================================
					// O SET ETERNO NAO SE APAGA AQUI (a licao que a `--esferateste` ja registrou: bancada
					// que estraga o mundo mede o destroco que ela mesma fez). So os de jogador saem.
					_sets.RemoveAll(s => !s.Eterno);
					_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
					_invocacoes.Clear();
					if (!_sets.Any(s => s.Eterno)) ErguerOSetEterno();

					SetDeEsferas? eterno = _sets.Find(s => s.Eterno);
					Checa("(montagem) o set ETERNO esta de pe, em Namek",
						  eterno != null && string.Equals(eterno.ZonaNome, Esferas.PlanetaEterno,
														  StringComparison.OrdinalIgnoreCase),
						  eterno?.ZonaNome ?? "(nao existe)");
					Checa($"(montagem) ...com as {Esferas.Total} esferas dele",
						  eterno != null && _esferas.Count(e => e.Set == eterno.Id) == Esferas.Total,
						  eterno == null ? "-" : $"{_esferas.Count(e => e.Set == eterno.Id)}");

					if (eterno == null) { GD.PrintErr("  a bancada precisa do set eterno pra medir; abortando."); return; }

					// O TESTADOR VIRA NAMEKUSEIJIN DO CLA DO DRAGAO E GUARDIAO DA TERRA -- nao ha atalho
					// pro `ErguerEstatua`, ele e chamado com os tres portoes ligados. Tudo volta no fim.
					pl.Race = "Namekian";
					pl.Class = "Dragon clan";
					_tronos["guardian"] = pl.Conta;

					// O BP VIRA O `WishPower` DA ESTATUA (`namekian.dm:97`), e e ele que abre o patamar do
					// desejo de curar planeta. Sem isto a familia 5 falharia pelo motivo errado -- por um
					// personagem de bancada nascer com BP baixo, e nao pela regra que ela veio medir.
					pl.Ficha.BP = Math.Max(pl.Ficha.BP, Esferas.PoderDoEterno);

					MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
					ErguerEstatua(pl, "");

					SetDeEsferas? daTerra = _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);
					Checa("(montagem) o verbo de producao ergueu a Estatua do Dragao na TERRA",
						  daTerra != null);
					if (daTerra == null) { GD.PrintErr("  sem o set da Terra nao ha 'esferas de outro lugar'; abortando."); return; }

					int cicloDaTerra = daTerra.Ciclo;
					Checa($"(montagem) ...com as {Esferas.Total} esferas dele",
						  _esferas.Count(e => e.Set == daTerra.Id) == Esferas.Total,
						  $"{_esferas.Count(e => e.Set == daTerra.Id)}");

					// O ARQUIVO NASCE NA PASTA TEMPORARIA: a familia 4 recarrega do disco, e sem esta
					// gravacao ela leria a AUSENCIA do arquivo -- e mediria um mundo recem-nascido.
					SalvarEsferas();

					// =====================================================================
					// 1. O PORTAO DE ENTRADA -- as duas metades, mesmo corpo, mesmo planeta
					// =====================================================================
					DerrubarAEstatua(pl, "sim");
					Checa("(montagem) a estatua da Terra saiu do caminho pro teste do portao",
						  !_sets.Any(s => s.Zona.Hash == terra.Hash));

					ComecarMorteLenta(terra, 1, "bancada-porunga-portao");
					ouviu.Clear();
					ErguerEstatua(pl, "");

					Checa("**NAO SE ERGUE ESTATUA NUM MUNDO QUE ESTA ACABANDO**",
						  !_sets.Any(s => s.Zona.Hash == terra.Hash), "a estatua subiu no planeta condenado");
					Checa("...e o jogador OUVE por que (o aviso, e nao a intencao)",
						  ouviu.Exists(a => a.Contains("acabando", StringComparison.OrdinalIgnoreCase)),
						  string.Join(" | ", ouviu));

					// A OUTRA METADE: abortada a morte, o MESMO gesto passa. Sem ela, "o portao recusa"
					// ficaria verde num verbo que recusasse sempre.
					AbortarMorte(terra, "bancada-porunga-portao");
					ErguerEstatua(pl, "");
					daTerra = _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);
					Checa("...e com o planeta VIVO de novo o mesmo gesto ergue a estatua",
						  daTerra != null);
					if (daTerra == null) { GD.PrintErr("  sem o set da Terra nao da pra seguir; abortando."); return; }
					cicloDaTerra = daTerra.Ciclo;

					// =====================================================================
					// 2. O ZELADOR DO ETERNO ESTA VIVO -- a metade que derruba "ele nao volta sozinho"
					// =====================================================================
					// `ManterOSetEterno` E QUEM GARANTIA O OPOSTO DO PEDIDO DO DONO: uma vez por segundo
					// ele cura inercia, poder, numero de desejos, prazo e esfera perdida do set eterno --
					// era literalmente *"o unico set do jogo que nunca morre de vez"*. Antes de afirmar
					// (familia 5) que ele NAO ressuscita o Porunga num mundo morto, e preciso ve-lo
					// fazendo exatamente isso num mundo VIVO. Sem esta metade, "o tique nao o trouxe de
					// volta" ficaria verde num tique que nao roda, num zelador deletado e num servidor
					// parado -- que e a definicao de afirmacao verde em sistema morto.
					//
					// A SONDA E O PROPRIO OBJETO DE PRODUCAO: o set eterno e estragado a mao (inerte,
					// poder 1, sem nenhuma das sete) e a leitura e NELE, depois de UM tique de verdade.
					eterno.Inerte = true;
					eterno.Poder = 1;
					_esferas.RemoveAll(e => e.Set == eterno.Id);
					Checa("(montagem) o set eterno foi estragado a mao: inerte, poder 1, sem esfera nenhuma",
						  eterno.Inerte && eterno.Poder == 1 && !_esferas.Any(e => e.Set == eterno.Id));

					TickDasEsferas();

					Checa("**COM NAMEK VIVA, UM TIQUE REFAZ O PORUNGA INTEIRO** (o zelador esta de pe)",
						  !eterno.Inerte && eterno.Poder == Esferas.PoderDoEterno
						  && eterno.Desejos == Esferas.DesejosDoEterno
						  && _esferas.Count(e => e.Set == eterno.Id) == Esferas.Total,
						  $"inerte={eterno.Inerte}, poder={eterno.Poder}, desejos={eterno.Desejos}, "
						  + $"esferas={_esferas.Count(e => e.Set == eterno.Id)}");

					// =====================================================================
					// 3. A METADE VIVA -- com Namek de pe o Porunga EXISTE, esta la, e ATENDE
					// =====================================================================
					// O CONTRA-EXEMPLO DO "SUMIU". Sem este bloco, *"o Porunga nao existe e nao pode ser
					// invocado"* ficaria igualmente verde num jogo sem esfera nenhuma. O gesto medido aqui
					// e o MESMO que a familia 4 repete depois da explosao -- as mesmas sete, na mesma mao,
					// do mesmo jogador, pelo mesmo verbo. So o planeta muda entre os dois.

					// O RELOGIO DO MUNDO ANDA, E NAO O CARIMBO DO SET (mesma disciplina da familia 7): a
					// espera de nascimento e regra de producao e ja e medida pela `--esferateste`. O tique
					// extra e pro teto do zelador (`TetoDeEsperaEterna`) se aplicar antes da conta.
					TickDasEsferas();
					_adiantoDoCeu += Math.Max(0, eterno.AtivoEm - TempoDoMundo) + 5;
					Checa("(montagem) passada a espera, o set ETERNO esta ACORDADO", SetAtivo(eterno),
						  $"faltam {(eterno.AtivoEm - TempoDoMundo) / 3600.0:0.#} h");

					// AS SETE DE NAMEK NA MAO (pra invocar) E UMA DA TERRA JUNTO (a montagem da familia 4:
					// so com uma de cada set no MESMO corpo, no MESMO instante, "a esfera do mundo morto
					// some" deixa de ficar verde num sistema que esvazia a mao de todo mundo).
					foreach (Esfera e in _esferas.Where(x => x.Set == eterno.Id).ToList())
					{
						e.Portador = pl.Id;
						e.PorZona(pl.Zone);
						e.X = pl.Pos.X;
						e.Y = pl.Pos.Y;
					}
					Esfera? deNamekNaMao = _esferas.Find(e => e.Set == eterno.Id);
					Esfera? daTerraNaMao = _esferas.Find(e => e.Set == daTerra.Id);
					if (daTerraNaMao != null)
					{
						daTerraNaMao.Portador = pl.Id;
						daTerraNaMao.PorZona(pl.Zone);
						daTerraNaMao.X = pl.Pos.X;
						daTerraNaMao.Y = pl.Pos.Y;
					}
					Checa($"(montagem) o testador carrega as {Esferas.Total} de Namek E uma da Terra",
						  QuantasCarrega(pl.Id) == Esferas.Total + 1, $"{QuantasCarrega(pl.Id)}");

					anunciou.Clear();
					InvocarODragao(pl);
					Checa("**COM NAMEK VIVA O PORUNGA ATENDE** -- o dragao sobe pelo verbo de producao",
						  _invocacoes.ContainsKey(eterno.Id), "o dragao dos sonhos nao subiu");
					Checa("...e o mundo ouve o nome dele (o anuncio, e nao a intencao)",
						  anunciou.Exists(a => a.Contains("PORUNGA", StringComparison.Ordinal)),
						  string.Join(" | ", anunciou));

					// =====================================================================
					// 4. A MORTE, PELO COMMIT DE PRODUCAO
					// =====================================================================
					anunciou.Clear();
					ouviu.Clear();

					Checa("(montagem) Namek esta VIVA antes do tiro", !ZonaMorta(namek));

					bool comecou = ComecarDestruicao(namek, 1, "bancada-porunga");
					Checa("(montagem) a destruicao de Namek comecou pelo verbo de producao", comecou);

					// UM SEGUNDO POR VOLTA, SEM ATALHO -- o mesmo laco que a `--planetateste` usa. Quem
					// chama `ConsumarDestruicao` e o tique, e nao a bancada.
					int voltas = 0;
					while (!ZonaMorta(namek) && voltas < 4000) { TickDaDestruicao(1); voltas++; }
					Checa("Namek esta DESTRUIDA depois do pavio inteiro", ZonaMorta(namek), $"{voltas} voltas");

					Checa("**O SET ETERNO NAO EXISTE MAIS** (o Porunga morreu com o planeta)",
						  !_sets.Any(s => s.Eterno), "ainda ha um set eterno em pe");
					Checa("...e nenhuma das sete dele sobrou no mundo",
						  !_esferas.Any(e => e.Set == eterno.Id),
						  $"{_esferas.Count(e => e.Set == eterno.Id)} esferas orfas");

					// A METADE QUE IMPEDE ISTO DE FICAR VERDE NUM SISTEMA QUE APAGA TUDO.
					SetDeEsferas? terraDepois = _sets.Find(s => s.Zona.Hash == terra.Hash);
					Checa("**E O SET DA TERRA ATRAVESSA A EXPLOSAO INTEIRO** (a morte e do mundo do set)",
						  terraDepois != null && terraDepois.Ciclo == cicloDaTerra
						  && _esferas.Count(e => e.Set == terraDepois.Id) == Esferas.Total,
						  terraDepois == null ? "sumiu" : $"ciclo {terraDepois.Ciclo} (era {cicloDaTerra}), "
												 + $"{_esferas.Count(e => e.Set == terraDepois.Id)} esferas");

					// ---- a esfera que estava na mao ----
					Checa("**A ESFERA DE NAMEK SOME DA MAO DE QUEM A CARREGAVA**",
						  deNamekNaMao != null && !_esferas.Contains(deNamekNaMao));
					Checa("...e a esfera da TERRA continua na mao dele (o crivo e o SET, nao a mao)",
						  daTerraNaMao != null && _esferas.Contains(daTerraNaMao)
						  && daTerraNaMao.Portador == pl.Id);
					Checa($"...e a conta do que ele carrega caiu de {Esferas.Total + 1} pra 1",
						  QuantasCarrega(pl.Id) == 1, $"{QuantasCarrega(pl.Id)}");
					Checa("...e ele foi AVISADO -- perda calada seria pior que perda",
						  ouviu.Exists(a => a.Contains("vira pó", StringComparison.OrdinalIgnoreCase)),
						  string.Join(" | ", ouviu));
					Checa("...e o mundo inteiro soube que o Porunga nao atende mais",
						  anunciou.Exists(a => a.Contains("não atende", StringComparison.OrdinalIgnoreCase)),
						  string.Join(" | ", anunciou));

					// ---- o dragao que estava DE PE, e o que acontece com quem tenta de novo ----
					// O PORUNGA ESTAVA ERGUIDO NA FAMILIA 3, com as sete na mao deste jogador. Um dragao
					// de pe pendurado num set que nao existe mais e o buraco classico: o `MandarEsferas`
					// desenharia um dragao orfao e o `db_desejar` acharia a invocacao pra um `SetPorId`
					// nulo -- ou seja, daria pra PEDIR ao Porunga depois de Namek explodir.
					Checa("**O DRAGAO QUE ESTAVA DE PE CAI COM O PLANETA**",
						  !_invocacoes.ContainsKey(eterno.Id), "a invocacao do eterno sobreviveu ao mundo");

					anunciou.Clear();
					InvocarODragao(pl);
					Checa("**E O PORUNGA NAO PODE MAIS SER INVOCADO** -- o mesmo verbo, a mesma mao, "
						+ "e agora nada sobe",
						  !_invocacoes.ContainsKey(eterno.Id));
					Checa("...e ninguem ouve o nome dele (na familia 3 este mesmo gesto o levantou)",
						  !anunciou.Exists(a => a.Contains("PORUNGA", StringComparison.Ordinal)),
						  string.Join(" | ", anunciou));

					// =====================================================================
					// 5. ELE NAO VOLTA SOZINHO -- o tique nao ressuscita o que a explosao enterrou
					// =====================================================================
					// ERA O `ManterOSetEterno` QUE GARANTIA O OPOSTO, e a familia 2 acabou de ve-lo curar
					// o Porunga inteiro num tique so. Aqui o MESMO laco roda um minuto de mundo com Namek
					// morta -- e a terceira checagem e a que impede isto de ficar verde num tique que
					// virou no-op: o set da Terra tem que atravessar os sessenta intacto.
					int esferasAntesDoLaco = _esferas.Count;
					for (int t = 0; t < 60; t++) TickDasEsferas();

					Checa("**SESSENTA TIQUES DEPOIS O PORUNGA CONTINUA MORTO** (o zelador nao o ressuscita)",
						  !_sets.Any(s => s.Eterno), "o tique trouxe o set eterno de volta");
					Checa("...e nenhuma das sete dele reapareceu no mundo",
						  !_esferas.Any(e => e.Set == eterno.Id),
						  $"{_esferas.Count(e => e.Set == eterno.Id)} esferas");
					Checa("...e o tique nao ficou mudo por acidente: o set da Terra atravessou os 60 inteiro",
						  _esferas.Count == esferasAntesDoLaco
						  && _sets.Count(s => s.Zona.Hash == terra.Hash) == 1,
						  $"{_esferas.Count} esferas (eram {esferasAntesDoLaco}), "
						  + $"{_sets.Count(s => s.Zona.Hash == terra.Hash)} set(s) na Terra");

					// ---- A ORDEM DENTRO DO TIQUE, LIDA NO OBJETO E NAO NA INTENCAO ----
					// O cabecalho do `TickDasEsferas` diz que o ENTERRO vem antes do ZELADOR, senao um
					// Porunga ancorado num cadaver teria as sete refeitas neste segundo e enterradas no
					// proximo -- *"um set piscando dentro de um planeta que nao existe"*. No fim do tique
					// as duas ordens dao a MESMA lista, entao ler a lista nao separa uma da outra. O que
					// separa e se o zelador chegou a TOCAR no set: a sonda volta pra `_sets` estragada, e
					// a leitura e nela depois do tique.
					var sonda = new SetDeEsferas
					{
						Id = eterno.Id,
						Eterno = true,
						Inerte = true,
						Poder = 1,
						Desejos = 1,
						NomeDoDragao = "PORUNGA",
						Dragao = "porunga",
						Folha = "namek",
					};
					sonda.PorZona(namek);
					_sets.Add(sonda);

					TickDasEsferas();

					Checa("**UM PORUNGA FORJADO NUM CADAVER SAI DA LISTA NO PRIMEIRO TIQUE**",
						  !_sets.Contains(sonda) && !_sets.Any(s => s.Eterno));
					Checa("...sem as sete terem sido refeitas nem por um segundo",
						  !_esferas.Any(e => e.Set == sonda.Id),
						  $"{_esferas.Count(e => e.Set == sonda.Id)} esferas");
					Checa("...e o zelador NAO chegou a tocar nele -- na familia 2 o MESMO estrago foi curado "
						+ "(o enterro vem antes do zelador, e da pra ver isso no objeto)",
						  sonda.Inerte && sonda.Poder == 1 && sonda.Desejos == 1,
						  $"inerte={sonda.Inerte}, poder={sonda.Poder}, desejos={sonda.Desejos}");

					// =====================================================================
					// 6. O REINICIO NAO DESENTERRA -- a porta da CARGA DO DISCO
					// =====================================================================
					// O CENARIO E REAL E NAO ACADEMICO: um `esferas.json` com o Porunga dentro enquanto
					// Namek esta morta nasce de um backup restaurado, de uma gravacao perdida ou de um
					// `planetas-mortos.json` mexido a mao. Aqui ele e forjado do jeito mais honesto que
					// existe -- devolvendo a lista o set que acabou de ser enterrado -- e gravado no disco
					// (temporario) pelo `SalvarEsferas` de producao.
					_sets.Add(eterno);
					for (int n = 1; n <= Esferas.Total; n++)
					{
						var e = new Esfera { Set = eterno.Id, Numero = n };
						e.PorZona(namek);
						_esferas.Add(e);
					}
					SalvarEsferas();
					Checa("(montagem) o arquivo foi forjado com o Porunga vivo num planeta morto",
						  _sets.Any(s => s.Eterno));

					CarregarEsferas();
					Checa("**REINICIAR NAO DESENTERRA O PORUNGA** (a carga do disco enterra de novo)",
						  !_sets.Any(s => s.Eterno), "o set eterno voltou so por reiniciar o servidor");
					Checa("...e nem as sete dele voltam",
						  !_esferas.Any(e => e.Set == eterno.Id),
						  $"{_esferas.Count(e => e.Set == eterno.Id)} esferas");

					// A CARGA APAGOU AS REFERENCIAS: os objetos de agora vieram do JSON.
					daTerra = _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);
					Checa("(montagem) o set da Terra voltou do disco inteiro",
						  daTerra != null && _esferas.Count(e => daTerra != null && e.Set == daTerra.Id) == Esferas.Total,
						  daTerra == null ? "sumiu" : $"{_esferas.Count(e => e.Set == daTerra.Id)} esferas");
					if (daTerra == null) { GD.PrintErr("  sem o set da Terra nao da pra pedir o desejo; abortando."); return; }

					// =====================================================================
					// 7. A VOLTA -- PELO DESEJO, COM AS ESFERAS DE OUTRO LUGAR
					// =====================================================================
					JuntarAsSete(pl, daTerra);
					Checa("(montagem) as sete da Terra estao com o testador",
						  QuantasCarrega(pl.Id) == Esferas.Total, $"{QuantasCarrega(pl.Id)}");

					// O RELOGIO DO MUNDO ANDA, e nao o carimbo do set: a espera de nascimento e regra de
					// producao e ja e medida pela `--esferateste`. Mexer no `AtivoEm` aqui seria desligar
					// aquela regra pra medir esta.
					_adiantoDoCeu += Math.Max(0, daTerra.AtivoEm - TempoDoMundo) + 5;
					Checa("(montagem) passada a espera, o set da Terra esta ACORDADO", SetAtivo(daTerra));

					InvocarODragao(pl);
					Checa("o dragao da Terra esta de pe (verbo de producao)",
						  _invocacoes.ContainsKey(daTerra.Id));

					Checa("**'curar um planeta' ESTA no menu de um set de jogador**",
						  MenuDaqui(pl, daTerra).Any(d => d.Id == "curar_planeta"),
						  string.Join(",", MenuDaqui(pl, daTerra).Select(d => d.Id)));

					// A METADE QUE RECUSA, PRIMEIRO: pedir a cura de um mundo VIVO nao restaura nada e
					// **nao consome pedido**. Sem ela, "o desejo restaurou" ficaria verde num desejo que
					// restaurasse qualquer nome que recebesse.
					ouviu.Clear();
					PedirDesejo(pl, "curar_planeta Earth");
					Checa("pedir a cura de um mundo VIVO nao gasta o pedido",
						  daTerra.Pedidos == 0 && _invocacoes.ContainsKey(daTerra.Id),
						  $"pedidos={daTerra.Pedidos}, dragao de pe={_invocacoes.ContainsKey(daTerra.Id)}");
					Checa("...e o jogador ouve o motivo",
						  ouviu.Exists(a => a.Contains("mundos mortos", StringComparison.OrdinalIgnoreCase)),
						  string.Join(" | ", ouviu));

					// E AGORA O PEDIDO DE VERDADE, PELO FUNIL DE PRODUCAO.
					anunciou.Clear();
					PedirDesejo(pl, $"curar_planeta {Esferas.PlanetaEterno}");

					Checa("**O DESEJO DE UM SET DE OUTRO LUGAR RESTAUROU NAMEK**", !ZonaMorta(namek));
					Checa("...e o pedido foi COBRADO (o desejo que funciona custa)",
						  daTerra.Pedidos == 1, $"{daTerra.Pedidos}");

					SetDeEsferas? porungaDeVolta = _sets.Find(s => s.Eterno);
					Checa("**E O PORUNGA VOLTOU** -- so pela restauracao, e sem nada ter sido guardado",
						  porungaDeVolta != null);
					Checa("...em Namek, e so la",
						  porungaDeVolta != null && string.Equals(porungaDeVolta.ZonaNome, Esferas.PlanetaEterno,
																  StringComparison.OrdinalIgnoreCase),
						  porungaDeVolta?.ZonaNome ?? "-");
					Checa($"...com as {Esferas.Total} esferas de novo espalhadas",
						  porungaDeVolta != null && _esferas.Count(e => e.Set == porungaDeVolta.Id) == Esferas.Total,
						  porungaDeVolta == null ? "-" : $"{_esferas.Count(e => e.Set == porungaDeVolta.Id)}");
					Checa($"...com os {Esferas.DesejosDoEterno} pedidos do eterno (ele volta INTEIRO, e nao mutilado)",
						  porungaDeVolta != null && porungaDeVolta.Desejos == Esferas.DesejosDoEterno
						  && porungaDeVolta.Poder == Esferas.PoderDoEterno);
					Checa("...e APAGADO -- ele renasce do zero, com a espera de nascimento (o preco da morte)",
						  porungaDeVolta != null && !SetAtivo(porungaDeVolta),
						  porungaDeVolta == null ? "-" : $"faltam {(porungaDeVolta.AtivoEm - TempoDoMundo) / 3600.0:0.#} h");

					// ---- E AGORA A MESMA VOLTA, ATRAVESSANDO O DISCO ----
					// TUDO ACIMA FOI LIDO NA MEMORIA. O que o jogador vai encontrar amanha e o que
					// sobreviver ao ARQUIVO -- e a familia 6 acabou de mostrar que a carga tem um portao
					// que ENTERRA. Se aquele portao errasse a pergunta (`ZonaCondenada` no lugar de
					// `ZonaMorta`, o livro dos mortos lido depois do erguer, uma zona comparada por nome),
					// o Porunga voltaria pelo desejo e sumiria de novo no primeiro reinicio -- calado.
					// Mesmos metodos do boot, `SalvarEsferas` e `CarregarEsferas`.
					SalvarEsferas();
					string bytesNoDisco = System.IO.File.Exists(CaminhoDasEsferas)
						? System.IO.File.ReadAllText(CaminhoDasEsferas) : "";
					Checa("(disco) o `esferas.json` da pasta de teste guarda o dragao de Namek",
						  bytesNoDisco.Contains("PORUNGA", StringComparison.Ordinal),
						  $"{bytesNoDisco.Length} bytes");

					CarregarEsferas();
					SetDeEsferas? doDisco = _sets.Find(s => s.Eterno);
					Checa("**E ELE ATRAVESSA O DISCO** -- reiniciar depois da restauracao nao o perde de novo",
						  doDisco != null, "o reinicio comeu o Porunga restaurado");
					Checa($"...em Namek, com as {Esferas.Total}, os {Esferas.DesejosDoEterno} desejos e o poder do eterno",
						  doDisco != null
						  && string.Equals(doDisco.ZonaNome, Esferas.PlanetaEterno, StringComparison.OrdinalIgnoreCase)
						  && _esferas.Count(e => doDisco != null && e.Set == doDisco.Id) == Esferas.Total
						  && doDisco.Desejos == Esferas.DesejosDoEterno
						  && doDisco.Poder == Esferas.PoderDoEterno,
						  doDisco == null ? "-" : $"{doDisco.ZonaNome}, "
							  + $"{_esferas.Count(e => e.Set == doDisco.Id)} esferas, {doDisco.Desejos} desejos, "
							  + $"poder {doDisco.Poder}");
					Checa("...com os pedidos ZERADOS e a espera ainda de pe (ele RENASCE, nao ressurge)",
						  doDisco != null && doDisco.Pedidos == 0 && !SetAtivo(doDisco),
						  doDisco == null ? "-" : $"pedidos={doDisco.Pedidos}, "
							  + $"faltam {(doDisco.AtivoEm - TempoDoMundo) / 3600.0:0.#} h");

					SetDeEsferas? terraDoDisco = _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);
					Checa("...e o set da TERRA atravessa junto, com o pedido gasto que ele pagou",
						  terraDoDisco != null && terraDoDisco.Pedidos == 1
						  && _esferas.Count(e => terraDoDisco != null && e.Set == terraDoDisco.Id) == Esferas.Total,
						  terraDoDisco == null ? "sumiu" : $"pedidos={terraDoDisco.Pedidos}, "
							  + $"{_esferas.Count(e => e.Set == terraDoDisco.Id)} esferas");

					// A DIFERENCA ENTRE "VOLTOU" E "NUNCA MORREU": juntando as sete dele, o verbo responde
					// que **elas ainda se refazem** -- resposta de um set que existe e descansa. Um set
					// morto daria a outra resposta ("nao ha Esferas do Dragao aqui"), que foi exatamente o
					// que a familia 4 mediu.
					if (doDisco != null)
					{
						JuntarAsSete(pl, doDisco);
						ouviu.Clear();
						InvocarODragao(pl);
						Checa("...e quem junta as sete dele ouve 'ainda se refazem' -- a resposta de um set "
							+ "VIVO que descansa, e nao a de um set que nao existe",
							  ouviu.Exists(a => a.Contains("se refazem", StringComparison.OrdinalIgnoreCase))
							  && !_invocacoes.ContainsKey(doDisco.Id),
							  string.Join(" | ", ouviu));
					}

					// =====================================================================
					// 8. O SET DE JOGADOR NAO VOLTA -- E A ZONA FICA LIVRE
					// =====================================================================
					// ARLIA E A COBAIA (pre-feita, no manifesto, sem povo): aqui o que se mede e a
					// assimetria, e nao a explosao -- e ela nao pode custar os habitantes de um mundo
					// habitado.
					if (CorpoDaZona(arlia) is { } corpoDeArlia)
					{
						MoveToZone(pl.Id, arlia, PontoDeNascimento(arlia));
						FincarDominio(pl, corpoDeArlia, pl.Pos);
						ErguerEstatua(pl, "");

						SetDeEsferas? deArlia = _sets.Find(s => s.Zona.Hash == arlia.Hash);
						Checa("(montagem) o testador dominou Arlia e ergueu a estatua dele la",
							  deArlia != null);

						// FORA DO PLANETA ANTES DA EXPLOSAO: o commit fere e evacua quem esta la, e o que
						// esta sendo medido e o set, e nao o corpo do testador.
						MoveToZone(pl.Id, terra, PontoDeNascimento(terra));

						ComecarDestruicao(arlia, 1, "bancada-porunga-jogador");
						voltas = 0;
						while (!ZonaMorta(arlia) && voltas < 4000) { TickDaDestruicao(1); voltas++; }

						Checa("**O SET DE JOGADOR TAMBEM MORRE COM O PLANETA** (a regra e uma so)",
							  !_sets.Any(s => s.Zona.Hash == arlia.Hash));

						RessuscitarPlaneta(arlia);
						Checa("**...E ELE NAO VOLTA COM A RESTAURACAO** (estatua de alguem nao e derivavel "
							+ "de constante -- so o eterno e)",
							  !_sets.Any(s => s.Zona.Hash == arlia.Hash),
							  "voltou um set que ninguem sabe reconstruir");
						Checa("...e o Porunga continua de pe (restaurar OUTRO mundo nao o toca)",
							  _sets.Any(s => s.Eterno));

						// A METADE QUE FECHA O LIMBO DA FASE 0: com o set apagado (e nao inerte), a zona
						// desbloqueia e o mesmo Namekuseijin ergue outra. Um set inerte trancaria o mundo
						// pra sempre -- `_sets.Any(s => s.Zona.Hash == ...)` e o bloqueio de erguer.
						MoveToZone(pl.Id, arlia, PontoDeNascimento(arlia));
						ErguerEstatua(pl, "");
						Checa("**E A ZONA FICA LIVRE**: da pra erguer OUTRA estatua no mundo restaurado",
							  _sets.Any(s => s.Zona.Hash == arlia.Hash),
							  "a zona ficou trancada -- e o limbo que esta regra veio desfazer");
						MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
					}
					else Checa("(montagem) Arlia esta na carta estelar", false, "sem cobaia pra a familia 6");

					// =====================================================================
					// 9. AS DUAS FAMILIAS COM DEFEITO INJETADO (em memoria)
					// =====================================================================
					// A. A ANCORA MENTINDO. O criterio: matar Namek enterra o Porunga. Com a ancora dele
					//    reescrita pra um mundo vivo, o MESMO criterio tem que reprovar -- senao ele
					//    estaria verde tambem num sistema que apagasse todo set eterno a cada explosao.
					bool MorrerComNamekEnterra()
					{
						if (_sets.Find(s => s.Eterno) is not { } alvo) return false;
						ZoneKey guardada = alvo.Zona;

						ComecarDestruicao(namek, 1, "bancada-porunga-mutacao");
						int v = 0;
						while (!ZonaMorta(namek) && v < 4000) { TickDaDestruicao(1); v++; }

						bool enterrou = !_sets.Any(s => s.Eterno);

						// O MUNDO VOLTA ANTES DA RESPOSTA SUBIR: restaurar re-ergue o eterno pelo funil de
						// producao, e a ancora dele volta a ser Namek sozinha.
						RessuscitarPlaneta(namek);
						if (_sets.Find(s => s.Eterno) is { } novo) novo.PorZona(guardada);
						return enterrou;
					}

					// O SET E REENCONTRADO A CADA PASSO, E NAO GUARDADO NUMA VARIAVEL: o proprio criterio
					// mata e re-ergue o eterno, entao uma referencia colhida antes dele apontaria pro
					// objeto que ja foi enterrado -- e o "defeito injetado" seria injetado num fantasma.
					MutacaoDeEsfera(Checa,
						"matar Namek enterra o Porunga (medido pelo commit de producao)",
						"a ANCORA do set eterno reescrita pra um mundo vivo -- o `Ballplanet` torto do DM",
						MorrerComNamekEnterra,
						() => { if (_sets.Find(s => s.Eterno) is { } x) x.PorZona(terra); },
						() => { if (_sets.Find(s => s.Eterno) is { } x) x.PorZona(namek); });

					// B. O PORTAO DO NASCIMENTO. O criterio: a carga do disco ergue o Porunga. Com Namek
					//    no livro dos mortos, o MESMO criterio tem que reprovar -- e o gate visto mordendo.
					bool ACargaErgueOPorunga()
					{
						_sets.RemoveAll(s => s.Eterno);
						_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
						SalvarEsferas();
						CarregarEsferas();
						return _sets.Any(s => s.Eterno);
					}

					// O DEFEITO DESTA CARIMBA A FASE A MAO, e isso e deliberado: o que ela mede e a porta
					// da CARGA, e nao o commit (esse ja foi medido de ponta a ponta na familia 3). Passar
					// pelo commit de novo custaria os habitantes de Namek uma segunda vez, sem responder
					// nada que a familia 3 nao tenha respondido.
					MutacaoDeEsfera(Checa,
						"a carga do disco ergue o Porunga quando Namek esta viva",
						"Namek no livro dos mortos -- o portao de `ErguerOSetEterno`",
						ACargaErgueOPorunga,
						() =>
						{
							ComecarDestruicao(namek, 1, "bancada-porunga-mutacao-b");
							if (MorteDaZona(namek) is { } m) { m.Fase = FaseDaMorte.Destruido; m.Faltam = 0; }
						},
						() => RessuscitarPlaneta(namek));
				}
				finally { palco.Dispose(); }

				// ============================ O QUE SE MEDE E O QUE O PALCO BARROU ============================
				// `MatouAqui` compara o registro do fim com a foto do comeco, e esta bancada RESTAURA os
				// planetas que matou (e regra dela) -- entao ele sai zero mesmo depois de Namek e Arlia
				// explodirem. O numero honesto e o das GRAVACOES BARRADAS: cada uma e uma linha de
				// producao que teria reescrito o `planetas-mortos.json`, contada quando ela rodou.
				// =========================================================================================
				Checa($"**O PALCO BARROU {palco.EscritasBarradas} GRAVACOES DO LIVRO DOS MORTOS** -- "
					  + "palco que nunca cobre nada e indistinguivel de palco nenhum",
					  palco.EscritasBarradas > 0, $"{palco.EscritasBarradas}");

				GD.Print($"[bancada] o disco inteiro desta rodada foi pra '{caixa.PastaDeTeste}'");
			}
		}
		finally
		{
			// A ORDEM IMPORTA: os dois palcos ja fecharam la em cima, entao o livro dos mortos e o
			// `_store` ja voltaram ao que eram. So depois disso as listas do mundo voltam -- e a
			// gravacao final vai pro lugar de verdade, com o conteudo de verdade.
			_sets.Clear(); _sets.AddRange(setsGuardados);
			_esferas.Clear(); _esferas.AddRange(esferasGuardadas);
			_dominios.Clear(); _dominios.AddRange(dominiosGuardados);
			_invocacoes.Clear();
			_adiantoDoCeu = ceuGuardado;

			pl.Race = racaGuardada;
			pl.Class = classeGuardada;
			pl.Ficha.BP = bpGuardado;
			if (tronoGuardado.Length > 0) _tronos["guardian"] = tronoGuardado;
			else _tronos.Remove("guardian");

			MoveToZone(pl.Id, zonaGuardada, posGuardada);

			SalvarEsferas();
			SalvarConquista();
			Persistir(pl);

			EscutaDasEsferas = null;
			EscutaDeAvisos = null;
		}

		GD.Print($"===== BANCADA DO PORUNGA: {ok} OK, {falhou} FALHA =====\n");
	}
}
