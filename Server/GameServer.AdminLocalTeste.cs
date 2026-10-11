using Godot;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO ADMIN POR ENDERECO (`--adminlocalteste`) -- quem conecta da maquina do servidor e admin, ATE o
/// endereco deixar de distinguir alguem.
///
/// A regra esta no `ConferirAmbiguidadeDeHost` (`GameServer.Admin.cs`). Aqui tres clientes crus entram pela
/// porta (todos chegam de 127.0.0.1, como chegaria todo mundo atras de um tunel), e a bancada olha as DUAS
/// pontas: o que o servidor decidiu (`EhAdmin`) e o que VIAJOU -- os poderes de cada ficha lenta
/// (`S2C.Atributos`) que cada cliente recebeu, mandadas pelo tique de combate de producao.
///
///   1. (a) o dono entra sozinho: admin; (d) contas em SEQUENCIA nao desarmam -- um sai, outro entra, admin;
///   2. (b) o segundo entra COM o dono dentro: NENHUM dos dois tem admin por endereco, e o segundo NUNCA teve
///      (nenhuma ficha que chegou nele leva o bit); (c) o segundo SAI: a chave continua desligada -- nem o
///      dono, nem quem relogar, nem o proximo que entrar sozinho;
///   3. o servidor para e sobe: a chave volta armada (o desarme e da sessao);
///   4. COMO O DONO DE VERDADE CONTINUA ADMIN: sozinho, ele promove a propria conta pelo caminho do painel; a
///      marca fica no disco, sobrevive ao desarme e ao relog;
///   5. os CONTRA-EXEMPLOS: a ordem antiga (`AdminConcedeAntesDeConferirDeTeste`: o recem-chegado SAI ADMIN e
///      quem estava perde) e o caderno que nao esquecia (`AdminLembraDeQuemSaiuDeTeste`: contas em sequencia
///      desarmam);
///   6. (e) a chave desligada pelo `--sem-admin-local` continua desligada: ninguem e admin pelo endereco, a
///      parada NAO a rearma, e a conta marcada continua valendo.
///
/// Com `--sem-admin-local` na linha de comando (o caso `adminlocal_chave` do lancador), a bancada roda so a
/// familia 6, com a flag DE VERDADE lida no boot.
///
///     Godot --headless --path . --server --port 8287 --adminlocalteste [--sem-admin-local]
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
		GD.Print("[adminlocal] ================ O ADMIN POR ENDERECO, E QUANDO ELE DEIXA DE VALER (dono, 2026-10-11) ================");

		const string contaDoDono = "bancada_admin_dono", contaDoOutro = "bancada_admin_outro", contaDoTerceiro = "bancada_admin_terceiro";
		const uint bitDeAdmin = (uint)Protocol.Poder.Admin;
		var dono = new ClienteCruDeTeste();
		var outro = new ClienteCruDeTeste();
		var terceiro = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, dono, outro, terceiro);
		bool chaveDaLinhaDeComando = Array.IndexOf(OS.GetCmdlineArgs(), "--sem-admin-local") >= 0;

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
		bool SemAdminNasDuasPontas(ServerPlayer? corpo, ClienteCruDeTeste cli) =>
			corpo != null && !EhAdmin(corpo) && cli.FichasLentas > 0 && (cli.UltimosPoderes & bitDeAdmin) == 0;
		bool ParaESobe()
		{
			Stop();
			mesa.Bombeia(() => !dono.Conectado && !outro.Conectado && !terceiro.Conectado, 4000);
			return Start(porta);
		}

		try
		{
			if (chaveDaLinhaDeComando)
			{
				GD.Print("[adminlocal] -- 6) com `--sem-admin-local` NA LINHA DE COMANDO: a chave nasce desligada, e fica --");
				ChaveDesligada("a flag de verdade, lida no boot");
				return;
			}

			// ---------------------------------------------------------------- 1. sozinho, e em sequencia
			GD.Print("[adminlocal] -- 1) (a) o dono sozinho; (d) contas em SEQUENCIA --");
			AfirmarAl("(montagem) o servidor acabou de subir com a chave armada", Running && AdminPorEndereco && !_enderecoAmbiguo && _contas.Count == 0);
			ServerPlayer? corpoDoDono = Entra(dono, contaDoDono, "Dono");
			AfirmarAl("**(a) O DONO ENTRA SOZINHO, DA MAQUINA DO SERVIDOR: E ADMIN** -- no servidor e na ficha que chegou no cliente dele",
					  AdminPorEndereco && AdminNasDuasPontas(corpoDoDono, dono), $"{dono.FichasLentas} ficha(s) lenta(s) recebida(s)");

			bool donoSaiu = Sai(dono, contaDoDono);
			ServerPlayer? corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl("**(d) CONTAS EM SEQUENCIA NAO DESARMAM**: o dono saiu, outra conta entra sozinha -- e admin, e a chave continua armada",
					  donoSaiu && AdminPorEndereco && !_enderecoAmbiguo && AdminNasDuasPontas(corpoDoOutro, outro));
			bool outroSaiu = Sai(outro, contaDoOutro);
			corpoDoDono = Entra(dono, contaDoDono, "Dono");
			AfirmarAl("...e de volta: a segunda saiu, o dono entra de novo e continua admin", outroSaiu && AdminPorEndereco && AdminNasDuasPontas(corpoDoDono, dono));

			// ---------------------------------------------------------------- 2. ao mesmo tempo
			GD.Print("[adminlocal] -- 2) (b) o segundo entra COM o dono dentro; (c) o segundo sai --");
			corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl("**(b) DUAS CONTAS LOCAIS AO MESMO TEMPO: NENHUMA TEM ADMIN POR ENDERECO** -- a chave desarma, o dono perde (no servidor e na ficha seguinte) e quem chegou nao recebe",
					  !AdminPorEndereco && _enderecoAmbiguo && SemAdminNasDuasPontas(corpoDoDono, dono) && SemAdminNasDuasPontas(corpoDoOutro, outro),
					  $"dono admin: {(corpoDoDono == null ? "sem corpo" : EhAdmin(corpoDoDono))}; quem chegou admin: {(corpoDoOutro == null ? "sem corpo" : EhAdmin(corpoDoOutro))}");
			AfirmarAl("**...E QUEM DISPAROU O DESARME NUNCA TEVE O BIT, NEM POR UM PACOTE**: nenhuma das fichas lentas que chegaram nesta conexao dele leva o admin",
					  outro.FichasLentas > 0 && (outro.PoderesJaVistos & bitDeAdmin) == 0 && corpoDoOutro != null && (corpoDoOutro.PoderesConcedidos & Protocol.Poder.Admin) == 0,
					  $"{outro.FichasLentas} ficha(s) lenta(s); poderes ja vistos: {outro.PoderesJaVistos}");

			outroSaiu = Sai(outro, contaDoOutro);
			AfirmarAl("**(c) O SEGUNDO SAI, E A CHAVE CONTINUA DESLIGADA** (a regra mais segura: o que a ambiguidade provou continua verdade): o dono segue sem admin por endereco",
					  outroSaiu && !AdminPorEndereco && _contas.Count == 1 && SemAdminNasDuasPontas(corpoDoDono, dono));
			donoSaiu = Sai(dono, contaDoDono);
			corpoDoDono = Entra(dono, contaDoDono, "Dono");
			AfirmarAl("...nem relogando sozinho", donoSaiu && !AdminPorEndereco && SemAdminNasDuasPontas(corpoDoDono, dono));
			donoSaiu = Sai(dono, contaDoDono);
			ServerPlayer? corpoDoTerceiro = Entra(terceiro, contaDoTerceiro, "Terceiro");
			AfirmarAl("...nem o proximo que entrar sozinho (atras de um tunel, seria um estranho)", donoSaiu && SemAdminNasDuasPontas(corpoDoTerceiro, terceiro));

			// ---------------------------------------------------------------- 3. o desarme e da sessao
			GD.Print("[adminlocal] -- 3) o servidor para e sobe: a chave volta armada --");
			AfirmarAl("(montagem) o servidor para e sobe de novo no mesmo processo", ParaESobe() && Running);
			corpoDoDono = Entra(dono, contaDoDono, "Dono");
			AfirmarAl("depois de reiniciar o servidor a chave esta armada de novo, e o dono sozinho e admin", AdminPorEndereco && !_enderecoAmbiguo && AdminNasDuasPontas(corpoDoDono, dono));

			// ---------------------------------------------------------------- 4. a marca na conta
			GD.Print("[adminlocal] -- 4) como o dono de verdade continua admin: a MARCA NA CONTA --");
			if (corpoDoDono != null) AdminPromover(corpoDoDono, contaDoDono, virar: true);   // o caminho do painel de contas
			AfirmarAl("(montagem) sozinho e admin, o dono promove a propria conta pelo painel: a marca vai pro disco",
					  _store?.Carregar(contaDoDono)?.Admin == true);
			corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl("**COM A CONTA MARCADA, O DONO NAO PERDE NADA NO DESARME**: a chave cai, quem chegou nao recebe, e o dono continua admin (o admin dele nao vem do endereco)",
					  !AdminPorEndereco && SemAdminNasDuasPontas(corpoDoOutro, outro) && AdminNasDuasPontas(corpoDoDono, dono));
			donoSaiu = Sai(dono, contaDoDono);
			corpoDoDono = Entra(dono, contaDoDono, "Dono");
			AfirmarAl("...e continua admin relogando com a chave desligada", donoSaiu && !AdminPorEndereco && AdminNasDuasPontas(corpoDoDono, dono));

			// ---------------------------------------------------------------- 5. os contra-exemplos
			GD.Print("[adminlocal] -- 5) com os DEFEITOS INJETADOS: a ordem antiga, e o caderno que nao esquecia --");
			AfirmarAl("(montagem) o servidor para e sobe de novo", ParaESobe() && Running && AdminPorEndereco);
			corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			AdminConcedeAntesDeConferirDeTeste = true;
			corpoDoTerceiro = Entra(terceiro, contaDoTerceiro, "Terceiro");
			AdminConcedeAntesDeConferirDeTeste = false;
			AfirmarAl("(defeito injetado: concede antes de conferir) **O RECEM-CHEGADO SAI ADMINISTRADOR** -- no servidor e na ficha que chegou nele -- e quem ja estava no mundo perde. A regua sabe reprovar",
					  !AdminPorEndereco && AdminNasDuasPontas(corpoDoTerceiro, terceiro) && SemAdminNasDuasPontas(corpoDoOutro, outro),
					  $"quem chegou admin: {(corpoDoTerceiro == null ? "sem corpo" : EhAdmin(corpoDoTerceiro))}; poderes ja vistos nele: {terceiro.PoderesJaVistos}");

			AfirmarAl("(montagem) o servidor para e sobe de novo", ParaESobe() && Running && AdminPorEndereco);
			corpoDoOutro = Entra(outro, contaDoOutro, "Outro");
			bool eraAdmin = AdminNasDuasPontas(corpoDoOutro, outro);
			outroSaiu = Sai(outro, contaDoOutro);
			AdminLembraDeQuemSaiuDeTeste = contaDoOutro;
			corpoDoTerceiro = Entra(terceiro, contaDoTerceiro, "Terceiro");
			AdminLembraDeQuemSaiuDeTeste = null;
			AfirmarAl("(defeito injetado: o caderno nao esquece quem saiu) **CONTAS EM SEQUENCIA DESARMAM**: a primeira saiu, a segunda entra sozinha e fica sem admin, com a chave desligada. A regua sabe reprovar",
					  eraAdmin && outroSaiu && !AdminPorEndereco && _contas.Count == 1 && SemAdminNasDuasPontas(corpoDoTerceiro, terceiro));

			// ---------------------------------------------------------------- 6. a chave da linha de comando
			GD.Print("[adminlocal] -- 6) a chave desligada como o `--sem-admin-local` desliga (a mesma funcao que o boot chama) --");
			AfirmarAl("(montagem) o servidor para e sobe de novo, com a chave armada", ParaESobe() && Running && AdminPorEndereco);
			DesligarAdminPorEndereco();
			ChaveDesligada("a funcao que o boot chama pra flag");
		}
		catch (Exception e)
		{
			AfirmarAl("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			AdminConcedeAntesDeConferirDeTeste = false;
			AdminLembraDeQuemSaiuDeTeste = null;
			mesa.Fecha();
			_adminLocalRodando = false;
			GD.Print($"[adminlocal] ================ {_alOk} OK, {_alFalhou} FALHA(S) ================");
		}

		// (e) A CHAVE DESLIGADA: ninguem e admin pelo endereco, sozinho ou acompanhado; nada "desarma" (nao ha o que
		// desarmar); a parada NAO a rearma; e a conta marcada, se houver, continua valendo.
		void ChaveDesligada(string como)
		{
			AfirmarAl($"(montagem) a chave esta desligada ({como}) e nao e por ambiguidade", !AdminPorEndereco && !_enderecoAmbiguo);
			ServerPlayer? a = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl("**(e) COM A CHAVE DESLIGADA, QUEM ENTRA SOZINHO DA MAQUINA DO SERVIDOR NAO E ADMIN**", SemAdminNasDuasPontas(a, outro) && (outro.PoderesJaVistos & bitDeAdmin) == 0);
			ServerPlayer? t = Entra(terceiro, contaDoTerceiro, "Terceiro");
			AfirmarAl("...um segundo entra junto: ninguem ganha, ninguem perde, e nada e marcado como ambiguidade",
					  SemAdminNasDuasPontas(a, outro) && SemAdminNasDuasPontas(t, terceiro) && !AdminPorEndereco && !_enderecoAmbiguo);
			AfirmarAl("(montagem) o servidor para e sobe de novo", ParaESobe() && Running);
			AfirmarAl("**...E A PARADA NAO REARMA O QUE A LINHA DE COMANDO DESLIGOU**: a chave continua desligada na subida seguinte", !AdminPorEndereco && !_enderecoAmbiguo);
			a = Entra(outro, contaDoOutro, "Outro");
			AfirmarAl("...e quem entra sozinho continua sem admin", SemAdminNasDuasPontas(a, outro));
			if (_store?.Carregar(contaDoDono)?.Admin == true)
			{
				ServerPlayer? marcado = Entra(dono, contaDoDono, "Dono");
				AfirmarAl("...enquanto a CONTA MARCADA continua admin com a chave desligada (\"so a marca na conta vale\")", AdminNasDuasPontas(marcado, dono));
			}
		}
	}
}
