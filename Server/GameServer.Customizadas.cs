using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A CRIACAO DE TECNICAS DE KI -- o porte de `Code/Modules/Skills/CustomAttacks/customattacks.dm`.
///
/// ============================ E PORTE, E O SISTEMA INTEIRO ESTAVA LA ============================
/// O jogador monta ate 10 tecnicas, gasta 5 pontos em cada uma, escolhe tipo, nome, descricao e os
/// dois gritos, e a tecnica vira um verbo `Custom_Attack&lt;id&gt;` que ATIRA. Nada disso foi
/// inventado aqui: as regras estao no `Core.Skills.TecnicaCustomizada`, citadas linha a linha, e o
/// disparo entra pela mesma porta que as tres tecnicas de projetil ja portadas
/// (`GameServer.Disparar` / `GameServer.Canalizar`).
///
/// AS TRES COISAS QUE ESTE ARQUIVO E:
///   1. os verbos (`Create_Attack`, `Customize_Attack`, `Forget_Attack` do DM, `:222-236`), pelo
///      canal unico `C2S.Verbo` com prefixo `ca_`;
///   2. o DISPARO (`CustomAttackHandler`, `:396`; `CustomBlastFire`, `:514`; `CustomGuidedFire`,
///      `:525`);
///   3. o pacote que alimenta a tela de montagem.
///
/// O QUE NAO FOI PORTADO, e por que:
///   * ENSINAR tecnica customizada -- `Teach_Custom_Attack` esta COMENTADO no original (`:238-242`
///     e `:258-270`), junto do `generateteachableskills`. Nunca funcionou la.
///   * ICONES e AURAS custom -- o painel do DM deixa escolher um `.dmi` das pastas do jogo. Aqui o
///     projetil e desenhado com a COR DO KI DO DONO (decisao da camada 1: `ReceitaDeProjetil` nao
///     tem campo de cor, e o cabecalho dela explica que uma segunda resposta pra "de que cor e o ki
///     deste sujeito" foi o que fez o `Client/Aura.cs` existir). Um seletor de icone aqui exigiria
///     primeiro um catalogo de artes de projetil, que o port nao tem.
///   * MELEE (`attacktype 3`) -- tem tela e nao tem disparo no DM. Mesma razao pela qual ele nao
///     esta no <see cref="TipoDeProjetil"/>.
/// ============================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// AS TECNICAS DO DISCO. Save antigo (campo nulo) = ninguem inventou nada -- ver a nota do
	/// `CharacterSave.Customizadas`.
	///
	/// SANEIA NA CARGA e nao na hora de usar, pelo mesmo motivo do `Mochila.Sanear`: um id fora de
	/// 1..10 (save mexido a mao, formato de outra versao) viraria um verbo que o despacho nao
	/// reconhece e um botao que nao faz nada. Prefere-se perder a entrada quebrada a carregar uma
	/// tecnica que nunca dispara.
	/// </summary>
	private static void PrepararCustomizadas(ServerPlayer pl, CharacterSave? save)
	{
		pl.Customizadas.Clear();
		pl.Mesa = null;
		if (save?.Customizadas == null) return;

		foreach (TecnicaCustomizada t in save.Customizadas)
		{
			if (t == null || t.Id < 1 || t.Id > TecnicaCustomizada.Maximo) continue;
			if (pl.Customizadas.Exists(o => o.Id == t.Id)) continue;
			t.Criada = true;   // do disco so sai tecnica confirmada; rascunho nunca e gravado
			pl.Customizadas.Add(t);
		}
		pl.Customizadas.Sort((a, b) => a.Id.CompareTo(b.Id));
	}

	// =====================================================================
	// OS VERBOS
	// =====================================================================
	/// <summary>
	/// O RAMO `ca_*` do canal unico de verbos. Devolve verdadeiro quando o comando era daqui.
	///
	/// TUDO RESPONDE COM A LISTA. Cada comando que muda alguma coisa termina em
	/// <see cref="MandarCustomizadas"/>: a tela nunca calcula ponto nenhum, ela desenha o que
	/// voltou. E o que impede a tabela de precos de existir em duas casas.
	/// </summary>
	private bool ComandoDeTecnicaCustomizada(ServerPlayer pl, string cmd, string arg)
	{
		if (!cmd.StartsWith("ca_", StringComparison.Ordinal)) return false;

		switch (cmd)
		{
			case "ca_listar": MandarCustomizadas(pl); return true;
			case "ca_criar": CriarTecnica(pl); return true;
			case "ca_editar": EditarTecnica(pl, arg); return true;
			case "ca_tipo": TipoDaTecnica(pl, arg); return true;
			case "ca_comprar": ComprarNaMesa(pl, arg); return true;
			case "ca_texto": TextoDaMesa(pl, arg); return true;
			case "ca_grito": AlternarGrito(pl, arg); return true;
			case "ca_arte": ArteDaTecnica(pl, arg); return true;
			case "ca_salvar": SalvarTecnica(pl); return true;
			case "ca_cancelar": CancelarMesa(pl); return true;
			case "ca_esquecer": EsquecerTecnica(pl, arg); return true;
		}

		Avisar(pl, "esse comando de tecnica nao existe.");
		return true;
	}

	/// <summary>
	/// CREATE_ATTACK (`:228`) -- abre a mesa com um rascunho novo.
	///
	/// ============================ O TETO DE DEZ, E ELE FALA ============================
	/// No DM o `createcustomizableskill` e um `switch(usr.customattacks.len)` com ramos de 0 a 9 e
	/// NENHUM `else`: com dez tecnicas na mao o verbo simplesmente nao faz nada -- nem janela, nem
	/// mensagem. O jogador aperta e nao acontece coisa alguma.
	///
	/// Aqui o teto recusa DIZENDO por que, e a bancada prova que ele dispara (familia 5). Um teto
	/// silencioso e indistinguivel de um botao quebrado: e a armadilha 5 da casa.
	/// ==============================================================================
	/// </summary>
	private void CriarTecnica(ServerPlayer pl)
	{
		if (pl.Customizadas.Count >= TecnicaCustomizada.Maximo)
		{
			Avisar(pl, $"voce ja tem as {TecnicaCustomizada.Maximo} tecnicas que cabem na sua cabeca -- "
					   + "esqueca uma antes de inventar outra.");
			return;
		}

		// O MENOR ID LIVRE, e nao `Count + 1`. Esquecendo a numero 2 de cinco tecnicas, o `len` do
		// DM cairia pra 4 e a proxima criacao reusaria o id 5 -- que ainda esta ocupado. La isso
		// nao aparece porque o verbo e o slot sao amarrados pelo mesmo `switch` torto; aqui o id e
		// a identidade da tecnica (e a chave do save), entao ele tem que ser livre de verdade.
		int id = 1;
		while (pl.Customizadas.Exists(t => t.Id == id)) id++;

		pl.Mesa = new TecnicaCustomizada { Id = id };
		pl.Mesa.PorTipo(TipoDeProjetil.Beam);
		Avisar(pl, "voce comeca a desenhar uma tecnica nova na cabeca.");
		MandarCustomizadas(pl);
	}

	/// <summary>
	/// CUSTOMIZE_ATTACK (`:222`) -- abre a mesa com uma COPIA da tecnica escolhida.
	///
	/// A copia e o `GenerateCustomAttackStats` do DM (`:619`): mexer sem estragar. Cancelar
	/// (`ca_cancelar`) joga a copia fora e a tecnica de pe continua como estava -- inclusive os
	/// pontos, que e o que mais dói perder.
	/// </summary>
	private void EditarTecnica(ServerPlayer pl, string arg)
	{
		TecnicaCustomizada? t = AcharTecnica(pl, arg);
		if (t == null) { Avisar(pl, "voce nao tem essa tecnica."); return; }

		pl.Mesa = t.Clonar();
		MandarCustomizadas(pl);
	}

	/// <summary>
	/// O TIPO, e ele SO SE ESCOLHE UMA VEZ.
	///
	/// `InitializeCustomize` desabilita o `attacktypebtn` (`:778`) e o proprio alerta do DM avisa:
	/// *"You cannot edit attack types after completion!"* (`:1205`). Faz sentido de jogo -- os
	/// pontos ja gastos em modificadores de raio (alcance, distancia, instantaneo) nao existem nos
	/// outros dois tipos, e trocar de tipo com eles pagos daria pontos de graca.
	/// </summary>
	private void TipoDaTecnica(ServerPlayer pl, string arg)
	{
		if (pl.Mesa is not { } m) { Avisar(pl, "abra uma tecnica antes."); return; }
		if (m.Criada)
		{
			Avisar(pl, "o tipo de um ataque nao muda depois de pronto -- so os numeros dele.");
			return;
		}

		TipoDeProjetil? t = arg.ToLowerInvariant() switch
		{
			"beam" or "raio" => TipoDeProjetil.Beam,
			"blast" or "bola" => TipoDeProjetil.Blast,
			"guided" or "teleguiado" => TipoDeProjetil.Guided,
			_ => null,
		};
		if (t == null) { Avisar(pl, "tipo desconhecido: use beam, blast ou guided."); return; }

		// ============================ SAIR DO RAIO DESFAZ OS MODIFICADORES DE RAIO ============================
		// Alcance, forca-pela-distancia, instantaneo e tempo de carga so existem no Beam
		// (`AddModifiers`, `:1306`). Sair do Beam com eles COMPRADOS deixaria pontos presos num
		// campo que nao vale mais nada; sair com eles REBAIXADOS deixaria os pontos que eles
		// renderam na mao, gastos em outra coisa, com o desconto sumindo junto com o tipo.
		//
		// E O SEGUNDO CASO E UM BURACO DE VERDADE -- eu escrevi este metodo com estorno solto
		// (`Aplicar(..., out _)`) e ele era silenciosamente ignorado quando nao cabia: baixar o
		// alcance pra 15 (+5 pontos), gastar 10 em velocidade e instantaneo, e trocar pra bola
		// deixava a tecnica com 2 pontos que ninguem pagou. Um estorno que pode falhar e um estorno
		// que TEM que ser conferido.
		//
		// Por isso o desfazer roda numa COPIA e so e adotado inteiro. No DM isto nao existe porque
		// o tipo nunca muda depois do primeiro clique; aqui ele muda enquanto for rascunho, e a
		// atomicidade e o preco dessa liberdade.
		// =================================================================================================
		if (t != TipoDeProjetil.Beam && m.Tipo == TipoDeProjetil.Beam)
		{
			TecnicaCustomizada tentativa = m.Clonar();
			if (!DesfazerModificadoresDeRaio(tentativa, out string falta))
			{
				Avisar(pl, $"pra virar {arg} e preciso desfazer os ajustes de raio primeiro, e {falta}");
				return;
			}
			m = tentativa;
			pl.Mesa = m;
		}

		m.PorTipo(t.Value);
		MandarCustomizadas(pl);
	}

	/// <summary>
	/// ESCOLHER A ARTE -- o `pick_game_icon` (`customattacks.dm:543-568`) e o unico lugar do jogo
	/// original em que um JOGADOR escolhe como o proprio ki se parece.
	///
	/// ============================ NAO CUSTA PONTO, E ISSO E DO ORIGINAL ============================
	/// O botao de icone do DM fica fora do orcamento de cinco pontos: ele nao passa pelo
	/// `custompoints_spent` em lugar nenhum. Cobrar aqui seria vender aparencia por poder, e o
	/// painel inteiro deste port existe pra nao ter uma segunda tabela de precos.
	/// ==========================================================================================
	///
	/// ============================ E A LISTA E RECORTADA PELO TIPO ============================
	/// `custom_icon_folders` (`:558-562`): raio ve `Beams`+`Techniques`, bola ve so `Blasts`,
	/// teleguiada ve so `Techniques`. **O gate e conferido aqui e nao so na tela**: o comando chega
	/// por texto no canal de verbos, e uma tela adulterada (ou de outra versao) poderia pedir uma
	/// folha de raio pra uma bola -- que sairia desenhada com a cauda de um beam.
	/// ===================================================================================
	/// </summary>
	private void ArteDaTecnica(ServerPlayer pl, string arg)
	{
		if (pl.Mesa is not { } m) { Avisar(pl, "abra uma tecnica antes."); return; }

		// VAZIO OU ZERO = VOLTAR AO PADRAO. E o `if(!choice) return null` do `pick_game_icon`, que
		// deixa `attackicon` nulo -- ou seja o jogador pode desfazer a escolha, e nao so trocar.
		if (!ushort.TryParse(arg, out ushort n)) { Avisar(pl, "arte desconhecida."); return; }

		var a = (ArteDeKi)n;
		if (a != ArteDeKi.Nenhuma && !ArteDeProjetil.PermitidasPara(m.Tipo).Contains(a))
		{
			Avisar(pl, "essa arte nao serve pra este tipo de ataque.");
			return;
		}

		m.Arte = a;
		MandarCustomizadas(pl);
	}

	/// <summary>
	/// DEVOLVE A TECNICA AOS PADROES DE RAIO -- alcance 20, modificador 1, sem instantaneo, carga 1.
	///
	/// Cada passo entra pelo funil de compra do `Core`, entao cada um paga (ou recebe) exatamente o
	/// que a tabela do DM manda. Falso = algum passo nao cabe no orcamento, e ai NADA foi mexido de
	/// util: quem chama trabalha numa copia.
	/// </summary>
	private static bool DesfazerModificadoresDeRaio(TecnicaCustomizada m, out string porque)
	{
		porque = "";
		if (m.Instantaneo && !m.Aplicar(Compra.InstantaneoDesligar, 0, out porque)) return false;

		if ((int)m.Alcance != (int)TecnicaCustomizada.AlcancePadrao
			&& !m.Aplicar(Compra.Alcance, TecnicaCustomizada.AlcancePadrao, out porque)) return false;

		if (Math.Abs(m.DistanciaMod - TecnicaCustomizada.DistModPadrao) > 1e-6
			&& !m.Aplicar(Compra.DistanciaMod, TecnicaCustomizada.DistModPadrao, out porque)) return false;

		// ============================ A CARGA VOLTA PELOS DEGRAUS, E NAO PELA CAIXINHA ============================
		// A tentacao e chamar `CarregavelDesligar`, que ja sabe a conta. Errado: ele cobra
		// `(carga-1)*2,5 + 1`, e esse "+1" e o preco da CAIXINHA -- que num raio nunca foi paga.
		// `PickAttackType` (`:874`) crava `chargeattack = 1` de graca ao escolher Beam, e `InitBeam`
		// (`:794`) desabilita a caixinha justamente pra ela nunca ser clicada num raio. Cobrar o +1
		// aqui seria cobrar por um estorno que ninguem recebeu.
		//
		// O que foi COMPRADO e o tempo, degrau a degrau -- entao e degrau a degrau que ele volta, pelo
		// mesmo funil. O `Carregavel` em si o `PorTipo` desliga de graca, exatamente como o DM.
		// =====================================================================================================
		for (int passo = 0; passo < 64 && Math.Abs(m.CargaMinima - TecnicaCustomizada.CargaPadrao) > 1e-6; passo++)
		{
			Compra c = m.CargaMinima > TecnicaCustomizada.CargaPadrao ? Compra.CargaMenos : Compra.CargaMais;
			if (!m.Aplicar(c, 0, out porque)) return false;
		}
		return true;
	}

	/// <summary>
	/// UMA COMPRA: `&lt;nome&gt;` ou `&lt;nome&gt;/&lt;valor&gt;` (alcance e modificador de distancia
	/// pedem numero, como o `input` do DM).
	///
	/// QUEM DECIDE E O `Core`. Este metodo so traduz o texto que veio do fio e devolve a lista --
	/// nenhuma guarda, nenhum preco e nenhum piso mora aqui. E o que permite a bancada percorrer a
	/// tabela inteira sem subir servidor.
	/// </summary>
	private void ComprarNaMesa(ServerPlayer pl, string arg)
	{
		if (pl.Mesa is not { } m) { Avisar(pl, "abra uma tecnica antes."); return; }

		string nome = arg;
		double valor = 0;
		int barra = arg.IndexOf('/');
		if (barra > 0)
		{
			nome = arg[..barra];
			double.TryParse(arg[(barra + 1)..], System.Globalization.NumberStyles.Float,
							System.Globalization.CultureInfo.InvariantCulture, out valor);
		}

		if (!Enum.TryParse(nome, ignoreCase: true, out Compra c))
		{
			Avisar(pl, "essa compra nao existe.");
			return;
		}

		if (!m.Aplicar(c, valor, out string porque)) Avisar(pl, porque);
		MandarCustomizadas(pl);
	}

	/// <summary>
	/// NOME, DESCRICAO E OS DOIS GRITOS: `&lt;campo&gt;/&lt;texto&gt;`.
	///
	/// CORTA em vez de recusar. O canal de verbos ja tem teto proprio (`MaxArgDeVerbo`), e o
	/// `GetString` do LiteNetLib devolve string VAZIA acima do limite em vez de truncar -- foi
	/// assim que o aviso do admin sumia inteiro (ver a nota do `MaxArgDeVerbo`). Aqui o corte e
	/// explicito e o jogador ve o que ficou.
	/// </summary>
	private void TextoDaMesa(ServerPlayer pl, string arg)
	{
		if (pl.Mesa is not { } m) { Avisar(pl, "abra uma tecnica antes."); return; }

		int barra = arg.IndexOf('/');
		if (barra <= 0) { Avisar(pl, "faltou o texto."); return; }
		string campo = arg[..barra];
		string texto = Limpar(arg[(barra + 1)..]).Trim();

		switch (campo.ToLowerInvariant())
		{
			case "nome":
				// NOME VAZIO NAO PASSA: ele vira o rotulo do botao e o nome no relato do golpe. Um
				// botao sem texto e um botao que ninguem aperta duas vezes.
				if (texto.Length == 0) { Avisar(pl, "a tecnica precisa de um nome."); return; }
				m.Nome = Cortar(texto, CustomWire.MaxNome);
				break;
			case "desc": m.Desc = Cortar(texto, CustomWire.MaxDesc); break;
			case "grito": m.Grito = Cortar(texto, CustomWire.MaxGrito); break;
			case "gritocarga": m.GritoDeCarga = Cortar(texto, CustomWire.MaxGrito); break;
			default: Avisar(pl, "campo desconhecido."); return;
		}
		MandarCustomizadas(pl);
	}

	/// <summary>
	/// OS DOIS INTERRUPTORES DE FALA. LIVRES -- `ShoutBeforeCheckbox`/`ShoutAfterCheckbox` (`:1076`,
	/// `:1087`) so escrevem a flag; nao ha ponto envolvido.
	/// </summary>
	private void AlternarGrito(ServerPlayer pl, string arg)
	{
		if (pl.Mesa is not { } m) { Avisar(pl, "abra uma tecnica antes."); return; }
		switch (arg.ToLowerInvariant())
		{
			case "grito": m.DizGrito = !m.DizGrito; break;
			case "gritocarga": m.DizGritoDeCarga = !m.DizGritoDeCarga; break;
			default: Avisar(pl, "campo desconhecido."); return;
		}
		MandarCustomizadas(pl);
	}

	/// <summary>
	/// CONFIRMAR (`CreateCustomSkill`, `:1200`): a mesa vira a tecnica de pe, e ela vai pro disco.
	///
	/// GRAVA NA HORA. No DM a tecnica so chega ao savefile no proximo `Save()` do mob, e quem
	/// desconectasse mal (crash, cabo) perdia a tecnica inteira e os pontos com ela. Uma tecnica e
	/// meia hora de fuçar num painel; ela nao pode depender de o jogador sair pela porta.
	/// </summary>
	private void SalvarTecnica(ServerPlayer pl)
	{
		if (pl.Mesa is not { } m) { Avisar(pl, "nao ha nada aberto pra confirmar."); return; }

		TecnicaCustomizada? existente = pl.Customizadas.Find(t => t.Id == m.Id);
		if (existente != null)
		{
			existente.CopiarDe(m);
		}
		else
		{
			// O TETO DE NOVO, AGORA NA CONFIRMACAO. `CriarTecnica` ja recusou, mas a mesa sobrevive
			// entre comandos: da pra abrir o rascunho com nove tecnicas, criar a decima por outro
			// caminho e so entao confirmar. Um teto que so vale numa das duas portas nao e teto --
			// e a mesma licao que fez `Disparar` repetir o limite de projeteis da `PodeAtirar`.
			if (pl.Customizadas.Count >= TecnicaCustomizada.Maximo)
			{
				Avisar(pl, $"voce ja tem as {TecnicaCustomizada.Maximo} tecnicas que cabem na sua cabeca.");
				return;
			}
			m.Criada = true;
			pl.Customizadas.Add(m);
			pl.Customizadas.Sort((a, b) => a.Id.CompareTo(b.Id));
			existente = m;
		}
		existente.Criada = true;

		pl.Mesa = null;
		Avisar(pl, $"{existente.Nome} esta pronta.");
		Persistir(pl);
		MandarCustomizadas(pl);
	}

	private void CancelarMesa(ServerPlayer pl)
	{
		pl.Mesa = null;
		MandarCustomizadas(pl);
	}

	/// <summary>
	/// FORGET_ATTACK (`:233`) -- e ele nao volta, exatamente como o alerta do DM avisa
	/// (*"This decision is irreversable!"*, `:330`).
	///
	/// O RAIO DE PE CAI JUNTO. Esquecer uma tecnica enquanto ela esta saindo da mao deixaria um
	/// canal alimentado por uma receita que nao existe mais -- e o canal so morre quando o dono
	/// solta, o que ele nao tem mais como fazer (o botao sumiu). Este projeto ja prendeu jogador
	/// dentro do proprio estado uma vez.
	/// </summary>
	private void EsquecerTecnica(ServerPlayer pl, string arg)
	{
		TecnicaCustomizada? t = AcharTecnica(pl, arg);
		if (t == null) { Avisar(pl, "voce nao tem essa tecnica."); return; }

		if (_canais.TryGetValue(pl.Id, out CanalDeKi? c) && c.Verbo == t.Verbo)
			FecharCanal(pl.Id, c, "a tecnica se desfaz na sua mao.");

		pl.Customizadas.Remove(t);
		if (pl.Mesa?.Id == t.Id) pl.Mesa = null;

		Avisar(pl, $"{t.Nome} sai da sua cabeca pra sempre.");
		Persistir(pl);
		MandarCustomizadas(pl);
	}

	private static TecnicaCustomizada? AcharTecnica(ServerPlayer pl, string arg)
		=> int.TryParse(arg, out int id) ? pl.Customizadas.Find(t => t.Id == id) : null;

	private static string Cortar(string s, int max) => s.Length <= max ? s : s[..max];

	// =====================================================================
	// O DISPARO
	// =====================================================================
	/// <summary>
	/// `CustomAttackHandler` (`:396`) -- o despacho por tipo, e o unico caminho pelo qual uma
	/// tecnica inventada sai da mao.
	///
	/// Devolve verdadeiro quando o verbo era um `Custom_Attack&lt;n&gt;`, mesmo que o disparo tenha
	/// sido recusado: quem manda o pacote na mao tem que ouvir o motivo, e nao cair no
	/// "voce nao sabe essa tecnica" generico -- que seria mentira, porque ele a inventou.
	/// </summary>
	private bool UsarTecnicaCustomizada(ServerPlayer pl, string verbo)
	{
		int id = TecnicaCustomizada.IdDoVerbo(verbo);
		if (id == 0) return false;

		TecnicaCustomizada? t = pl.Customizadas.Find(x => x.Id == id && x.Criada);
		// `if(!S) return` (`:402`). O DM sai calado; aqui o silencio seria um botao morto.
		if (t == null) { Avisar(pl, "voce nao tem uma tecnica nesse lugar."); return true; }

		if (t.Tipo == TipoDeProjetil.Beam) AtirarRaioCustom(pl, t);
		else AtirarBolaCustom(pl, t);
		return true;
	}

	/// <summary>
	/// BEAM: aperta pra carregar, aperta de novo pra soltar, e mais uma pra parar -- o mesmo estado
	/// do `Ki_Wave`, porque e o mesmo motor (`GameServer.Canalizar`).
	///
	/// ============================ A ESTAMINA VALE AQUI TAMBEM, E NO DM NAO VALIA ============================
	/// O `CustomShotOK` (`:481`) -- o unico lugar que confere e cobra estamina -- so e chamado pelo
	/// `CustomBlastFire` e pelo `CustomGuidedFire`. O fluxo do BEAM (`:410-475`) nunca o chama.
	///
	/// Ou seja: no original, ligar a estamina num raio DEVOLVE 2 pontos e nunca cobra nada. Sao dois
	/// pontos de graça -- 40% do orcamento -- justamente no tipo que tem os modificadores caros
	/// (instantaneo custa 2, alcance custa 1 por tile). Nao e uma escolha de design: e o ramo que
	/// ficou de fora quando os dois tipos novos foram acrescentados por cima do fluxo antigo.
	///
	/// O que se perdeu: um raio de estamina agora custa folego de verdade. O que se ganhou: a caixinha
	/// significa a mesma coisa nos tres tipos, e os 5 pontos voltam a ser 5.
	/// ====================================================================================================
	/// </summary>
	private void AtirarRaioCustom(ServerPlayer pl, TecnicaCustomizada t)
	{
		// `if(beaming) { canmove = 1; stopbeaming(); return }` -- a primeira linha de todo verbo de
		// raio do DM. Um raio canalizado nao e um disparo, e um estado.
		if (SoltarCanal(pl, t.Verbo)) return;

		double custo = t.CustoKi * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }
		if (!TemFolego(pl, t, out porque)) { Avisar(pl, porque); return; }

		if (t.DizGritoDeCarga && t.GritoDeCarga.Length > 0)
			Falar(pl, Protocol.Fala.Diz, t.GritoDeCarga);

		Canalizar(pl, t.Verbo, custo, t.Receita());

		// SO COBRA O FOLEGO SE O CANAL ABRIU. `Canalizar` refaz o `PodeAtirar` por conta propria
		// (ele e chamado direto por outras tecnicas), e entre a conferencia daqui e a de la nao ha
		// tique nenhum -- mas cobrar antes de saber se abriu seria pagar por um raio que nao saiu, e
		// e exatamente esse o defeito do `Guided_Ball` que a camada 1 consertou.
		if (t.UsaStamina && _canais.ContainsKey(pl.Id))
			pl.Ficha.stamina = Math.Max(0, pl.Ficha.stamina - t.CustoStamina);
	}

	/// <summary>
	/// BLAST e GUIDED: `CustomBlastFire` (`:514`) e `CustomGuidedFire` (`:525`). Saem na hora, nao
	/// prendem ninguem, e a diferenca entre os dois e uma linha -- o alvo marcado.
	/// </summary>
	private void AtirarBolaCustom(ServerPlayer pl, TecnicaCustomizada t)
	{
		double custo = t.CustoKi * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }
		if (!TemFolego(pl, t, out porque)) { Avisar(pl, porque); return; }

		ServerPlayer? alvo = null;
		if (t.Tipo == TipoDeProjetil.Guided)
		{
			alvo = Marcado(pl);
			// `if(T && !T.dead && T.z == z && T != src)` -- a mesma tripla do DM (`:533`).
			if (alvo == null || alvo == pl || alvo.Ficha.dead || !alvo.Zone.Equals(pl.Zone))
			{
				alvo = null;
				// O aviso e do original, literal (`:536`): sem ele o jogador acha que a tecnica
				// esta quebrada quando na verdade e ele que nao marcou ninguem.
				Avisar(pl, "sem alvo marcado: o ataque voa reto. (Marque alguem com duplo clique.)");
			}
		}

		if (t.DizGrito && t.Grito.Length > 0) Falar(pl, Protocol.Fala.Diz, t.Grito);

		pl.Ficha.Ki -= custo;
		if (t.UsaStamina) pl.Ficha.stamina = Math.Max(0, pl.Ficha.stamina - t.CustoStamina);

		// A PERICIA SOBE COMO EM QUALQUER TIRO. `Blast_Gain()` e o que faz atirar treinar quem
		// atira; uma tecnica inventada que nao treinasse seria um atalho pra ficar fraco.
		pl.Ficha.BlastGain(_rng);

		Projetil p = Disparar(pl, t.Receita());
		if (p.Vivo && alvo != null) p.Alvo = alvo.Id;
	}

	/// <summary>
	/// `if(S.use_stamina && stamina < S.stamina_cost)` (`:487`) -- e a mensagem tambem e a do DM.
	/// </summary>
	private static bool TemFolego(ServerPlayer pl, TecnicaCustomizada t, out string porque)
	{
		porque = "";
		if (!t.UsaStamina || pl.Ficha.stamina >= t.CustoStamina) return true;
		porque = $"voce esta cansado demais para usar {t.Nome}.";
		return false;
	}

	// =====================================================================
	// O PACOTE
	// =====================================================================
	/// <summary>
	/// A LISTA + A MESA ABERTA. Ver <see cref="Protocol.S2C.Customizadas"/> pra por que as duas
	/// coisas viajam juntas.
	/// </summary>
	private static void MandarCustomizadas(ServerPlayer pl)
	{
		if (pl.Peer == null) return;   // corpo de bancada / NPC: nao ha pra quem mandar

		NetDataWriter w = Protocol.Begin(Protocol.S2C.Customizadas);
		w.Put((byte)pl.Customizadas.Count);
		foreach (TecnicaCustomizada t in pl.Customizadas) CustomWire.Escrever(w, t);

		w.Put(pl.Mesa != null);
		if (pl.Mesa != null) CustomWire.Escrever(w, pl.Mesa);

		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
