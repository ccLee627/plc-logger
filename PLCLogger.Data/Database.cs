﻿using System;
using System.Collections.Generic;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Tool.hbm2ddl;
using PLCLogger.Entities;
using PLCLogger.Logic;
using PLCLogger.Messages;

namespace PLCLogger.Data
{
    public class Database : IDisposable
    {
        private readonly PLCInterface plcInterface;
        protected static Configuration cfg;
        protected static ISessionFactory sessionFactory = null;
        protected static ITransaction transaction = null;
        public static bool error;

        public Log MessageLog;
       
        public string quotes(string str)
        {
            return ("'" + str + "'");
        }

        public Database(PLCInterface plcInterface)
        {
            this.plcInterface = plcInterface;

            this.ConfigurarNHibernate();
            MessageLog = new Log("Database");

        }

     public void Dispose()
        {

        }


        //------------------------------------------------------------------------------------------------------------------
        bool EscribirDatosVariables(PLCInterface plc)
        {
            bool retval = true;
            plc.Variables_Escritura = new List<Variable>();
            try
            {
                using (ISession session = sessionFactory.OpenSession())
                using (ITransaction tx = session.BeginTransaction())
                {
                    string hql = "from Variable v where v.escribir=:escribir and v.instante_escritura>=:t_max_escritura";
                    IQuery query = session.CreateQuery(hql);
                    query.SetParameter("escribir", true);
                    //define un tiempo máximo dentro del cual la escritura debe ser efectuada
                    query.SetParameter("t_max_escritura", DateTime.Now.AddSeconds(-10));
                    var var_a_escribir = (List<Variable>)query.List<Variable>();
                    foreach (var v in var_a_escribir)
                    {
                        v.Address = Config.convertAdrress(v.Direccion, 2);
                        if (v.Type == "bit") v.Subaddress = Config.convertAdrress(v.Direccion, 3);
                        plc.Variables_Escritura.Add(v);

                    }

                    query = session.CreateQuery("from Variable v where v.escribir=:escribir");
                    query.SetParameter("escribir", true);
                    List<Variable> var_no_escritas = (List<Variable>)query.List<Variable>();

                    foreach (Variable v in var_no_escritas)
                    {
                        hql = "update Variable set ValorEscritura=:Valor, instante_escritura=:inst, escribir=:escribir where Name=:Name ";
                        query = session.CreateQuery(hql);
                        query.SetParameter("valor", null);
                        query.SetParameter("escribir", false);
                        query.SetParameter("name", v.Name);
                        query.SetParameter("inst", null);
                        query.ExecuteUpdate();
                    }
                    tx.Commit();
                }
            }


            catch (Exception ex)
            {
                MessageLog.Add(ex.Message);
                retval = false;
            }
            return retval;
        }


        //------------------------------------------------------------------------------------------------------------------
        bool GuardarDatosVariables(PLCInterface plc)
        {

            var retval = true;
            List<Variable> variables_db = null;
            using (var session = sessionFactory.OpenSession())
            using (var tx = session.BeginTransaction())
            {
                string hql = "from Variable";
                IQuery query = session.CreateQuery(hql).SetMaxResults(32000);
                variables_db = (List<Variable>)query.List<Variable>();
                tx.Commit();
            }

            // Lee las variables para comprobar si hay cambios
            // traer las variables de la db para ver si han cambiado

            try
            {

                var valores = new List<string>();
                var session = sessionFactory.OpenSession();
                using (var tx = session.BeginTransaction())
                {
                    for (int i = 0; i < plc.Variables.Count; i++)
                    {

                        plc.Variables[i].Fecha = DateTime.Now;
                        var variableDB = variables_db.Find(var => var.Name == plc.Variables[i].Name);

                        // si no está cargada, la crea
                        if (variableDB == null)
                        {
                            //testear inserts, nhibernate
                            session.Save(plc.Variables[i]);

                        }
                        else
                        {

                            //  session.Update(plc.Variables[i]);

                            string hql = "update Variable set address=:dir,type=:type,valor=:valor, Fecha=:Fecha where name=:name ";
                            var query = session.CreateQuery(hql);
                            query.SetParameter("valor", plc.Variables[i].Valor);
                            query.SetParameter("dir", plc.Variables[i].Address);
                            query.SetParameter("type", plc.Variables[i].Type);
                            query.SetParameter("Name", plc.Variables[i].Name);
                            query.SetParameter("fecha", plc.Variables[i].Fecha);

                            query.ExecuteUpdate();


                            //actualiza variables_log en caso de un cambio en el Valor

                            if (plc.Variables[i].Valor != variableDB.Valor)
                            {
                                var vl = new VariableLog
                                {
                                    Fecha = DateTime.Now,
                                    Valor = plc.Variables[i].Valor,
                                    Name = plc.Variables[i].Name
                                };
                                session.Save(vl);
                            }

                        }

                    }
                    tx.Commit();
                }

            }

            catch (Exception ex)
            {
                MessageLog.Add(ex.Message);
                retval = false;
            }

            return (retval);
        }

        public bool Sync(PLCInterface plc, Modos modo)
        {

            var retval = false;
            switch (modo)
            {
                case Modos.Guardar:
                    retval = GuardarDatosVariables(plc);
                    break;
                case Modos.LeerEscrituras:
                    retval = EscribirDatosVariables(plc);
                    break;
                default:
                    MessageLog.Add(": No se reconoce modo=" + modo);
                    break;
            }


            return (retval);
        }
        public void ConfigurarNHibernate()
        {
            try
            {
                cfg = new Configuration();
                cfg.Configure();
                var assembly = typeof(Variable).Assembly;
                cfg.AddAssembly(assembly);
                sessionFactory = cfg.BuildSessionFactory();
                //Crea las tablas con las columnas definidos en los *.hbm.xml. Si existen las deja como estaban.
                new SchemaUpdate(cfg).Execute(true, false);
                error = false;
            }
            catch (Exception e)
            {
                MessageLog.Add(e.Message);
            }
        }
    }
}
