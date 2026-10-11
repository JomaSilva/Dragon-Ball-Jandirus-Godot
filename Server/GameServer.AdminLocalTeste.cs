using Godot;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO ADMIN DE QUEM HOSPEDA (`--adminlocalteste`) -- o dono (2026-10-11): *"faca o adm ser so pra
/// quem clicou em hospedar"*.
///
/// A regra e a prova estao no `GameServer.Anfitriao.cs`. Aqui tres clientes crus entram pela porta, TODOS de
/// 127.0.0.1 (como chegaria todo mundo atras de um tunel), e a bancada olha as DUAS pontas: o que o servidor
/// decidiu (`EhAdmin`) e o que VIAJOU -- os poderes de cada ficha lenta (`S2C.Atributos`) que cada cliente
/// recebeu, mandadas pelo tique de combate de producao. O "cliente do mesmo processo" e o que pergunta a prova
/// ao servidor a cada discagem, pela mesma porta que o `GameClient` usa (`ProvaDoAnfitriaoPara`).
///
///   1. (d) SERVIDOR DEDICADO (a subida deste processo, `--server`): nao ha prova, e ninguem e admin -- nem o
///      primeiro que entra da propria maquina. Contra-exemplo: a regra antiga, por endereco
///      (`AdminPeloEnderecoDeTeste`), faz dele administrador.
///   2. HOSPEDAR (`SubirComAnfitriao`, a chamada do botao e do `--host`): (a) o cliente do mesmo processo e
///      admin; (b) um segundo cliente local, de outro soquete, NAO e -- nem por um pacote -- e o anfitriao nao
///      perde nada; (c) quem FORJA a prova (bytes ao acaso, so a marca, zeros, um byte trocado) entra como
///      jogador comum.
///   3. (e) PARAR E HOSPEDAR DE NOVO: a prova e outra, e a da hospedagem anterior nao vale. Contra-exemplo: o
///      `Stop` que nao esquece (`ParadaLembraDaSessaoDeTeste`) deixa a prova antiga abrindo a porta de admin
///      num servidor que subiu sem anfitriao.
///   4. (f) A MARCA NA CONTA continua valendo de qualquer conexao, hospedado ou dedicado -- e num servidor que
///      ele nao hospedou, quem hospedava antes nao e ninguem.
///   5. `--admin &lt;conta&gt;` pela funcao que a subida chama: conta que nao existe nao e promovida; a que existe
///      ganha a marca.
///
/// Com `--admin bancada_admin_dono` na linha de comando (o caso `adminlocal_dedicado` do lancador) a bancada
/// roda o servidor dedicado com a flag DE VERDADE: no boot a conta ainda nao existe (ninguem e promovido); ela
/// nasce no primeiro login; e na subida seguinte a flag a marca -- so ela.
///
///     Godot --headless --path . --server --port 8288 --adminlocalteste [--admin bancada_admin_dono]
/// </summary>
public partial class GameServer
{
	private int _alOk, _alFalhou;
	private bool _adminLocalRodando, _adminLocalJaRodou;

	private void AfirmarAl(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _alOk++; GD.Print($"[adminlocal]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _alFalhou++; GD.PrintErr($"[adminlocal]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoAdminLocal(int porta)
	{
		if (_adminLocalRodando || _adminLocalJaRodou) return;   // a subida que a propria bancada faz chega aqui de novo
		_adminLocalRodando = _adminLocalJaRodou = true;
		_alOk = _alFalhou = 0;
		GD.Print("[adminlocal] ================ O ADMIN E SO DE QUEM CLICOU EM HOSPEDAR (dono, 2026-10-11) ================");

		const string contaDoDono = "bancada_admin_dono", contaDoOutro = "bancada_admin_outro", contaDoForjador = "bancada_admin_forjador";
		const string esteComputador = "127.0.0.1";
		const uint bitDeAdmin = (uint)Protocol.Poder.Admin;
		var dono = new ClienteCruDeTeste();       // o cliente "do mesmo processo": pergunta a prova ao servidor a cada discagem
		var outro = new ClienteCruDeTeste();      // um cliente de fora: nao manda prova nenhuma
		var forjador = new ClienteCruDeTeste();   // um cliente de fora que manda o que quiser no lugar da prova
		var mesa = new MesaDeClientesCrus(this, porta, dono, outro, forjador);
		dono.ProvaAoDiscar = () => ProvaDoAnfitriaoPara(esteComputador, porta);
		bool comAFlag = Array.IndexOf(OS.GetCmdlineArgs(), FlagDoAdmin) >= 0;

		// Entra no mundo, roda o tique e devolve o corpo: dali em diante o cliente ja recebeu a ficha lenta dele.
		ServerPlayer? Entra(ClienteCruDeTeste cli, string conta, string nome)
		{
			ServerPlayer? corpo = mesa.EntraNoMundo(cli, conta, nome);
			mesa.TiqueEEntrega();
			return corpo;
		}
		bool Sai(ClienteCruDeTeste cli, string conta)
		{
			bool saiu = mesa.SaiDoJogo(cli, conta);
			mesa.TiqueEEntrega();
			return saiu;
		}
		bool AdminNasDuasPontas(ServerPlayer? corpo, ClienteCruDeTeste cli) =>
			corpo != null && EhAdmin(corpo) && cli.FichasLentas > 0 && (cli.UltimosPoderes & bitDeAdmin) != 0;
		// Sem admin no servidor, e NENHUMA ficha que chegou nesta conexao levou o bit -- nem por um pacote.
		bool NuncaFoiAdmin(ServerPlayer? corpo, ClienteCruDeTeste cli) =>
			corpo != null && !EhAdmin(corpo) && (corpo.PoderesConcedidos & Protocol.Poder.Admin) == 0
			&& cli.FichasLentas > 0 && (cli.PoderesJaVistos & bitDeAdmin) == 0;
		bool Para()
		{
			Stop();
			return mesa.Bombeia(() => !dono.Conectado && !outro.Conectado && !forjador.Conectado, 4000) && !Running;
		}
		bool Hospeda() => Para() && SubirComAnfitriao(porta);   // o gesto do botao "Hospedar partida" (e do `--host`)
		bool SobeDedicado() => Para() && Start(porta);          // o do `--server`: ninguem hospedou
		// O forjador sai (se estava dentro), troca o que manda no lugar da prova, e entra de novo.
		ServerPlayer? Forja(Func<byte[]?> prova)
		{
			if (mesa.CorpoDe(contaDoForjador) != null) Sai(forjador, contaDoForjador);
			forjador.ProvaAoDiscar = prova;
			return Entra(forjador, contaDoForjador, "Forjador");
		}

		try
		{
			if (comAFlag)
			{
				ODedicadoComAFlag();
				return;
			}

			// ---------------------------------------------------------------- 1. o servidor dedicado
			GD.Print("[adminlocal] -- 1) (d) SERVIDOR DEDICADO, sem `--admin`: ninguem hospedou --");
			AfirmarAl("(montagem) este servidor subiu pelo `--server`: nao ha prova de anfitriao, e ele nao entrega nenhuma a quem pede",
					  Running && _provaDoAnfitriao == null && ProvaDoAnfitriaoPara(esteComputador, porta) == null && _contas.Count == 0);
			ServerPlayer? corpoDoDono = Entra(dono, contaDoDono, "Dono");
			AfirmarAl("**(d) NUM SERVIDOR DEDICADO NAO HA ADMIN AUTOMATICO**: o primeiro que entra, sozinho e da propria maquina do servidor, e um jogador comum -- no servidor e em toda ficha que chegou nele",
					  NuncaFoiAdmin(corpoDoDono, dono), $"{dono.FichasLentas} ficha(s) lenta(s); poderes ja vistos: {dono.PoderesJaVistos}");
			AdminPeloEnderecoDeTeste = true;
			ServerPlayer? corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			AdminPeloEnderecoDeTeste = false;
			AfirmarAl("(defeito injetado: a regra antiga, por endereco) **QUEM CHEGA DE 127.0.0.1 -- COMO CHEGA TODO MUNDO ATRAS DE UM TUNEL -- ENTRA ADMINISTRADOR** num servidor que ninguem hospedou. A regua sabe reprovar",
					  AdminNasDuasPontas(corpoDoOutro, outro), $"poderes ja vistos nele: {outro.PoderesJaVistos}");

			// ---------------------------------------------------------------- 2. hospedar
			GD.Print("[adminlocal] -- 2) HOSPEDAR: (a) o cliente do mesmo processo; (b) um segundo cliente local; (c) quem forja a prova --");
			bool hospedou = Hospeda();
			byte[]? prova1 = ProvaDoAnfitriaoPara(esteComputador, porta)?.ToArray();
			AfirmarAl("(montagem) hospedar sorteia a prova desta hospedagem, e o servidor so a entrega a quem disca NELE MESMO: este computador (pelo numero ou por \"localhost\"), na porta que ele abriu -- nao pra outra porta, nem pro endereco de outra pessoa",
					  hospedou && prova1 is { Length: FioDaVersao.BytesDaProvaDeAnfitriao } && prova1.Any(x => x != 0)
					  && ProvaDoAnfitriaoPara("localhost", porta) != null
					  && ProvaDoAnfitriaoPara(esteComputador, porta + 1) == null && ProvaDoAnfitriaoPara("203.0.113.7", porta) == null
					  && ProvaDoAnfitriaoPara("servidor.de.outra.pessoa", porta) == null);
			corpoDoDono = Entra(dono, contaDoDono, "Dono");
			AfirmarAl("**(a) QUEM HOSPEDA E ADMIN**: o cliente do mesmo processo leva a prova no aperto de mao e entra administrador -- no servidor e na ficha que chegou nele",
					  AdminNasDuasPontas(corpoDoDono, dono) && EhHost(mesa.ParDe(dono)), $"{dono.FichasLentas} ficha(s) lenta(s)");
			corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl("**(b) UM SEGUNDO CLIENTE LOCAL NAO E ADMIN, NEM POR UM PACOTE**: outro soquete do mesmo computador, sem a prova, entra como jogador comum -- e quem hospeda NAO perde nada por isso",
					  NuncaFoiAdmin(corpoDoOutro, outro) && !EhHost(mesa.ParDe(outro)) && AdminNasDuasPontas(corpoDoDono, dono),
					  $"poderes ja vistos no segundo: {outro.PoderesJaVistos}");
			bool donoSaiu = Sai(dono, contaDoDono);
			bool outroSaiu = Sai(outro, contaDoOutro);
			corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl("...nem quando fica SOZINHO no servidor: o anfitriao saiu, o segundo sai e volta, e entrar sozinho da maquina do servidor nao da nada",
					  donoSaiu && outroSaiu && NuncaFoiAdmin(corpoDoOutro, outro) && _contas.Count == 1);

			int recusadasAntes = ProvasDeAnfitriaoRecusadas;
			var aoAcaso = new byte[FioDaVersao.BytesDaProvaDeAnfitriao];
			Random.Shared.NextBytes(aoAcaso);
			byte[] quase = prova1?.ToArray() ?? [];
			if (quase.Length > 0) quase[^1] ^= 1;
			bool forjouAoAcaso = NuncaFoiAdmin(Forja(() => aoAcaso), forjador);
			bool forjouSoAMarca = NuncaFoiAdmin(Forja(() => []), forjador);
			bool forjouZeros = NuncaFoiAdmin(Forja(() => new byte[FioDaVersao.BytesDaProvaDeAnfitriao]), forjador);
			bool forjouQuase = NuncaFoiAdmin(Forja(() => quase), forjador);
			AfirmarAl("**(c) QUEM FORJA A PROVA NAO E ADMIN**: 32 bytes ao acaso, so a marca (\"eu sou o anfitriao\", sem segredo), 32 zeros, e a prova de verdade com UM byte trocado -- os quatro entram como jogador comum, e o servidor conta as provas erradas",
					  forjouAoAcaso && forjouSoAMarca && forjouZeros && forjouQuase && ProvasDeAnfitriaoRecusadas == recusadasAntes + 3,
					  $"ao acaso: {forjouAoAcaso}; so a marca: {forjouSoAMarca}; zeros: {forjouZeros}; um byte trocado: {forjouQuase}; provas erradas contadas: {ProvasDeAnfitriaoRecusadas - recusadasAntes}");

			// ---------------------------------------------------------------- 3. parar e hospedar de novo
			GD.Print("[adminlocal] -- 3) (e) PARAR E HOSPEDAR DE NOVO: a prova anterior nao vale --");
			Sai(outro, contaDoOutro);
			Sai(forjador, contaDoForjador);
			bool parou = Para();
			AfirmarAl("**A PROVA MORRE NA PARADA**: servidor parado, nenhuma prova na memoria, e ele nao entrega nenhuma", parou && _provaDoAnfitriao == null && ProvaDoAnfitriaoPara(esteComputador, porta) == null);
			bool hospedouDeNovo = SubirComAnfitriao(porta);
			byte[]? prova2 = ProvaDoAnfitriaoPara(esteComputador, porta)?.ToArray();
			AfirmarAl("(montagem) hospedar de novo sorteia OUTRA prova", hospedouDeNovo && prova1 != null && prova2 != null && !prova1.SequenceEqual(prova2));
			AfirmarAl("**(e) A PROVA DA HOSPEDAGEM ANTERIOR NAO VALE**: quem a guardou e a manda agora entra como jogador comum",
					  NuncaFoiAdmin(Forja(() => prova1), forjador));
			corpoDoDono = Entra(dono, contaDoDono, "Dono");
			AfirmarAl("...e o cliente do mesmo processo, que pergunta a prova de novo a cada discagem, continua sendo o anfitriao", AdminNasDuasPontas(corpoDoDono, dono));

			Sai(dono, contaDoDono);
			Sai(forjador, contaDoForjador);
			ParadaLembraDaSessaoDeTeste = true;
			bool parouLembrando = Para();
			ParadaLembraDaSessaoDeTeste = false;
			bool subiuSemAnfitriao = Start(porta);
			ServerPlayer? comAProvaVelha = Forja(() => prova2);
			AfirmarAl("(defeito injetado: o `Stop` nao esquece a sessao) **A PROVA DA HOSPEDAGEM ANTERIOR CONTINUA ABRINDO A PORTA DE ADMIN** -- num servidor que subiu de novo SEM anfitriao, quem a guardou entra administrador. A regua sabe reprovar",
					  parouLembrando && subiuSemAnfitriao && AdminNasDuasPontas(comAProvaVelha, forjador));

			// ---------------------------------------------------------------- 4. a marca na conta
			GD.Print("[adminlocal] -- 4) (f) A MARCA NA CONTA continua valendo, de qualquer conexao --");
			AfirmarAl("(montagem) o servidor para (desta vez esquecendo) e e hospedado de novo", Hospeda() && Running);
			corpoDoDono = Entra(dono, contaDoDono, "Dono");
			if (corpoDoDono != null) AdminPromover(corpoDoDono, contaDoOutro, virar: true);   // o caminho do painel de contas
			AfirmarAl("(montagem) quem hospeda promove a conta de um amigo pelo painel: a marca vai pro disco",
					  AdminNasDuasPontas(corpoDoDono, dono) && _store?.Carregar(contaDoOutro)?.Admin == true);
			corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl("**(f) A CONTA MARCADA E ADMIN SEM PROVA NENHUMA**: o amigo entra de outro soquete, sem ser o anfitriao, e e administrador pela marca",
					  AdminNasDuasPontas(corpoDoOutro, outro) && !EhHost(mesa.ParDe(outro)));
			AfirmarAl("(montagem) o servidor para e sobe como DEDICADO (sem anfitriao)", SobeDedicado() && Running && _provaDoAnfitriao == null);
			corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			corpoDoDono = Entra(dono, contaDoDono, "Dono");
			AfirmarAl("...e num servidor dedicado a marca continua valendo -- enquanto quem hospedava ANTES, sem marca na conta dele, nao e ninguem",
					  AdminNasDuasPontas(corpoDoOutro, outro) && NuncaFoiAdmin(corpoDoDono, dono));

			// ---------------------------------------------------------------- 5. o `--admin` do dedicado
			GD.Print($"[adminlocal] -- 5) `{FlagDoAdmin} <conta>`: quem liga o servidor nomeia o administrador (a funcao que a subida chama) --");
			const string contaQueNaoExiste = "bancada_admin_que_nao_existe";
			bool promoveuQuemNaoExiste = PromoverAContaDaLinhaDeComando(["--server", FlagDoAdmin, contaQueNaoExiste]);
			bool promoveuSemNome = PromoverAContaDaLinhaDeComando(["--server", FlagDoAdmin]) || PromoverAContaDaLinhaDeComando([FlagDoAdmin, "--server"]);
			AfirmarAl($"**`{FlagDoAdmin}` SO MARCA CONTA QUE JA EXISTE**: um nome que ainda nao e de ninguem nao e promovido (quem o registrasse primeiro levaria o servidor) e nem e criado; sem nome, nada acontece",
					  !promoveuQuemNaoExiste && !promoveuSemNome && _store?.Carregar(contaQueNaoExiste) == null && !PromoverAContaDaLinhaDeComando(["--server", "--port", "7777"]));
			Sai(dono, contaDoDono);
			bool promoveu = PromoverAContaDaLinhaDeComando(["--server", FlagDoAdmin, contaDoDono]);
			corpoDoDono = Entra(dono, contaDoDono, "Dono");
			ServerPlayer? semMarca = Forja(() => null);
			AfirmarAl($"**COM `{FlagDoAdmin} conta`, SO ESSA CONTA**: a marca vai pro disco, a conta entra administradora num servidor dedicado, sem prova nenhuma -- e a conta ao lado, sem marca, continua comum",
					  promoveu && _store?.Carregar(contaDoDono)?.Admin == true && AdminNasDuasPontas(corpoDoDono, dono) && !EhHost(mesa.ParDe(dono))
					  && NuncaFoiAdmin(semMarca, forjador));
		}
		catch (Exception e)
		{
			AfirmarAl("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			AdminPeloEnderecoDeTeste = false;
			ParadaLembraDaSessaoDeTeste = false;
			mesa.Fecha();
			_adminLocalRodando = false;
			GD.Print($"[adminlocal] ================ {_alOk} OK, {_alFalhou} FALHA(S) ================");
		}

		// O SERVIDOR DEDICADO COM A FLAG DE VERDADE (`--admin bancada_admin_dono` na linha de comando deste processo,
		// numa pasta de saves vazia): a subida do boot ja leu a flag e nao achou a conta; a subida seguinte acha.
		void ODedicadoComAFlag()
		{
			GD.Print($"[adminlocal] -- com `{FlagDoAdmin} {contaDoDono}` NA LINHA DE COMANDO: o servidor dedicado e a flag de verdade --");
			AfirmarAl("(montagem) servidor dedicado, sem anfitriao, e a conta da flag ainda nao existe (a pasta de saves nasceu vazia)",
					  Running && _provaDoAnfitriao == null && _store?.Carregar(contaDoDono) == null);
			ServerPlayer? a = Entra(dono, contaDoDono, "Dono");
			ServerPlayer? b = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl("**A CONTA NAO EXISTIA QUANDO O SERVIDOR SUBIU: NINGUEM FOI PROMOVIDO** -- ela nasce agora, no primeiro login, e entra como jogador comum (o outro tambem)",
					  NuncaFoiAdmin(a, dono) && NuncaFoiAdmin(b, outro) && _store?.Carregar(contaDoDono)?.Admin == false);
			AfirmarAl("(montagem) o servidor para e sobe de novo: a subida rele a linha de comando", SobeDedicado() && Running);
			AfirmarAl("...e agora que a conta existe, a flag poe a marca nela, no disco", _store?.Carregar(contaDoDono)?.Admin == true && _store?.Carregar(contaDoOutro)?.Admin == false);
			a = Entra(dono, contaDoDono, "Dono");
			b = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl($"**COM `{FlagDoAdmin} conta`, SO ESSA CONTA E ADMIN** no servidor dedicado: ela entra administradora (sem ser anfitria de nada) e a outra continua comum",
					  AdminNasDuasPontas(a, dono) && !EhHost(mesa.ParDe(dono)) && NuncaFoiAdmin(b, outro));
		}
	}
}
